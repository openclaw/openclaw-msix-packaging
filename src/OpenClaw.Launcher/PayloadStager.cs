using System.Formats.Tar;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace OpenClaw.Launcher;

public sealed class PayloadStager
{
    private const int MaximumEntryCount = 250_000;
    private const long MaximumExtractedBytes = 8L * 1024 * 1024 * 1024;
    internal const string VerificationMarkerFileName = ".payload-verified-sha256";
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private readonly string _installDirectory;
    private readonly Action<string> _log;
    private readonly Action<string> _cleanupDirectory;

    public PayloadStager(
        string installDirectory,
        Action<string>? log = null)
        : this(installDirectory, log, DeleteDirectory)
    {
    }

    internal PayloadStager(
        string installDirectory,
        Action<string>? log,
        Action<string> cleanupDirectory)
    {
        ArgumentNullException.ThrowIfNull(cleanupDirectory);
        _installDirectory = Path.GetFullPath(installDirectory);
        _log = log ?? (_ => { });
        _cleanupDirectory = cleanupDirectory;
    }

    public async Task<StagedPayload> StageAsync(
        string payloadPath,
        string metadataPath,
        CancellationToken cancellationToken)
    {
        string fullPayloadPath = Path.GetFullPath(payloadPath);
        string fullMetadataPath = Path.GetFullPath(metadataPath);
        if (!File.Exists(fullPayloadPath))
        {
            throw new FileNotFoundException("OpenClaw payload was not found.", fullPayloadPath);
        }

        _log("Loading payload metadata.");
        PayloadMetadata metadata = await PayloadMetadata.LoadAsync(
            fullMetadataPath,
            cancellationToken);
        PayloadMetadata.ValidateForCurrentProcess(metadata, fullPayloadPath);

        _log("Verifying packaged payload SHA-256.");
        string actualHash = await ComputeHashAsync(fullPayloadPath, cancellationToken);
        if (!string.Equals(actualHash, metadata.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Payload SHA-256 does not match its metadata.");
        }

        string? installRoot = Path.GetDirectoryName(_installDirectory);
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            throw new InvalidOperationException(
                "The install directory must have a parent directory.");
        }

        Directory.CreateDirectory(installRoot);
        string installName = Path.GetFileName(_installDirectory);
        string temporaryDirectory = Path.Combine(installRoot, $".{installName}.staging");
        string backupDirectory = Path.Combine(installRoot, $".{installName}.previous");
        _log("Waiting for the exclusive installation lock.");
        var lockStopwatch = Stopwatch.StartNew();
        FileStream installLock = await InstallDirectoryLock.AcquireAsync(
            _installDirectory,
            cancellationToken);
        _log(
            $"Acquired the installation lock after {lockStopwatch.Elapsed.TotalSeconds:F1} seconds.");

        try
        {
            _log("Checking for an interrupted payload update.");
            RecoverInterruptedPromotion(
                _installDirectory,
                temporaryDirectory,
                backupDirectory);

            if (Directory.Exists(_installDirectory))
            {
                string? verifiedPayloadHash = await ReadVerificationMarkerAsync(
                    _installDirectory,
                    cancellationToken);
                if (string.Equals(
                    verifiedPayloadHash,
                    actualHash,
                    StringComparison.OrdinalIgnoreCase))
                {
                    _log(
                        "The installed payload marker matches; preserving user changes.");
                    return new StagedPayload(
                        _installDirectory,
                        actualHash,
                        Reused: true);
                }

                _log(
                    "The packaged payload changed; replacing the installed payload.");
            }

            using FileStream runtimeLock =
                PayloadRuntimeLock.AcquireForMutation(_installDirectory);
            _log("Extracting the verified payload. First launch can take several minutes.");
            Directory.CreateDirectory(temporaryDirectory);
            bool promoted = false;
            try
            {
                int extractedFileCount = await ReadPayloadAsync(
                    fullPayloadPath,
                    temporaryDirectory,
                    cancellationToken);
                _log($"Extracted {extractedFileCount} payload files.");
                EnsureOpenClawEntryPoint(temporaryDirectory);

                await WriteVerificationMarkerAsync(
                    temporaryDirectory,
                    actualHash,
                    cancellationToken);

                try
                {
                    _log("Promoting the staged payload into the stable install directory.");
                    if (Directory.Exists(_installDirectory))
                    {
                        Directory.Move(_installDirectory, backupDirectory);
                    }

                    Directory.Move(temporaryDirectory, _installDirectory);
                    promoted = true;
                    _log("Payload installation completed.");
                }
                catch
                {
                    if (!Directory.Exists(_installDirectory) &&
                        Directory.Exists(backupDirectory))
                    {
                        Directory.Move(backupDirectory, _installDirectory);
                    }

                    throw;
                }

                return new StagedPayload(
                    _installDirectory,
                    actualHash,
                    Reused: false);
            }
            finally
            {
                TryDeleteDirectory(temporaryDirectory);
                if (promoted)
                {
                    TryDeleteDirectory(backupDirectory);
                }
            }
        }
        finally
        {
            installLock.Dispose();
            _log("Released the installation lock.");
        }
    }

    private static void RecoverInterruptedPromotion(
        string installDirectory,
        string temporaryDirectory,
        string backupDirectory)
    {
        DeleteDirectory(temporaryDirectory);

        if (!Directory.Exists(installDirectory) &&
            Directory.Exists(backupDirectory))
        {
            Directory.Move(backupDirectory, installDirectory);
        }
        else if (Directory.Exists(installDirectory))
        {
            DeleteDirectory(backupDirectory);
        }
    }

    private static async Task<int> ReadPayloadAsync(
        string payloadPath,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        string rootPrefix = Path.GetFullPath(destinationRoot)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var seenPaths = new HashSet<string>(PathComparer);
        long extractedBytes = 0;
        int entryCount = 0;
        int extractedFileCount = 0;

        await using FileStream payloadStream = File.OpenRead(payloadPath);
        await using var gzipStream = new GZipStream(
            payloadStream,
            CompressionMode.Decompress,
            leaveOpen: false);
        using var reader = new TarReader(gzipStream, leaveOpen: false);

        TarEntry? entry;
        while ((entry = await reader.GetNextEntryAsync(
            copyData: false,
            cancellationToken)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++entryCount > MaximumEntryCount)
            {
                throw new InvalidDataException("Payload contains too many archive entries.");
            }

            string relativePath = NormalizeEntryPath(entry.Name);
            if (relativePath.Length == 0)
            {
                continue;
            }

            string destinationPath = Path.GetFullPath(
                Path.Combine(destinationRoot, relativePath));
            if (!destinationPath.StartsWith(
                    rootPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Payload entry escapes the staging directory: {entry.Name}");
            }

            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(destinationPath);
                    break;
                case TarEntryType.RegularFile:
                case TarEntryType.V7RegularFile:
                    if (!seenPaths.Add(relativePath))
                    {
                        throw new InvalidDataException(
                            $"Payload contains a duplicate file path: {entry.Name}");
                    }

                    extractedBytes = checked(extractedBytes + entry.Length);
                    if (extractedBytes > MaximumExtractedBytes)
                    {
                        throw new InvalidDataException("Payload is too large after extraction.");
                    }

                    if (entry.DataStream is null && entry.Length != 0)
                    {
                        throw new InvalidDataException(
                            $"Payload file has no data stream: {entry.Name}");
                    }

                    string? parentDirectory = Path.GetDirectoryName(destinationPath);
                    if (parentDirectory is not null)
                    {
                        Directory.CreateDirectory(parentDirectory);
                    }

                    await using (FileStream output = new(
                        destinationPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None))
                    {
                        if (entry.DataStream is not null)
                        {
                            await entry.DataStream.CopyToAsync(
                                output,
                                cancellationToken);
                        }
                    }

                    extractedFileCount++;
                    break;
                default:
                    throw new InvalidDataException(
                        $"Payload entry type is not supported: {entry.EntryType}");
            }
        }

        return extractedFileCount;
    }

    private static string NormalizeEntryPath(string entryName)
    {
        string normalized = entryName.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        normalized = normalized.TrimEnd('/');
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        if (normalized[0] == '/' ||
            Path.IsPathFullyQualified(normalized))
        {
            throw new InvalidDataException($"Payload entry path is absolute: {entryName}");
        }

        string[] segments = normalized.Split('/');
        foreach (string segment in segments)
        {
            if (segment.Length == 0 ||
                segment is "." or ".." ||
                segment.EndsWith(' ') ||
                segment.EndsWith('.') ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                IsReservedWindowsName(segment))
            {
                throw new InvalidDataException($"Payload entry path is unsafe: {entryName}");
            }
        }

        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    private static bool IsReservedWindowsName(string segment)
    {
        string baseName = segment.Split('.')[0];
        return baseName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            (baseName.Length == 4 &&
             (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
              baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
             baseName[3] is >= '1' and <= '9');
    }

    private static void EnsureOpenClawEntryPoint(string directory)
    {
        if (!File.Exists(Path.Combine(directory, "openclaw.mjs")))
        {
            throw new InvalidDataException("Payload does not contain openclaw.mjs.");
        }
    }

    private static async Task<string> ComputeHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static async Task<string?> ReadVerificationMarkerAsync(
        string installDirectory,
        CancellationToken cancellationToken)
    {
        string markerPath = Path.Combine(
            installDirectory,
            VerificationMarkerFileName);
        if (!File.Exists(markerPath) ||
            !File.Exists(Path.Combine(installDirectory, "openclaw.mjs")))
        {
            return null;
        }

        string marker = await File.ReadAllTextAsync(
            markerPath,
            cancellationToken);
        string value = marker.Trim();
        return value.Length == 64 && value.All(Uri.IsHexDigit)
            ? value
            : null;
    }

    private static async Task WriteVerificationMarkerAsync(
        string installDirectory,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        string markerPath = Path.Combine(
            installDirectory,
            VerificationMarkerFileName);
        string temporaryMarkerPath = markerPath + ".tmp";
        await File.WriteAllTextAsync(
            temporaryMarkerPath,
            payloadHash,
            cancellationToken);
        File.Move(temporaryMarkerPath, markerPath, overwrite: true);
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            _cleanupDirectory(path);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            _log(
                $"Could not remove payload cleanup directory '{path}': " +
                exception.Message);
        }
    }

}

public sealed record StagedPayload(
    string DirectoryPath,
    string PayloadSha256,
    bool Reused);
