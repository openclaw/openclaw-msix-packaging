using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;

namespace OpenClaw.Launcher;

public sealed record PayloadMetadata(
    [property: JsonPropertyName("repository")] string Repository,
    [property: JsonPropertyName("resolvedCommit")] string ResolvedCommit,
    [property: JsonPropertyName("architecture")] string Architecture,
    [property: JsonPropertyName("archive")] string Archive,
    [property: JsonPropertyName("sha256")] string Sha256)
{
    public static async Task<PayloadMetadata> LoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Payload metadata was not found.", path);
        }

        await using FileStream stream = File.OpenRead(path);
        PayloadMetadata? metadata = await JsonSerializer.DeserializeAsync(
            stream,
            OpenClawJsonContext.Default.PayloadMetadata,
            cancellationToken);

        if (metadata is null)
        {
            throw new InvalidDataException("Payload metadata is empty or invalid.");
        }

        metadata.Validate();
        return metadata;
    }

    private void Validate()
    {
        if (Repository != "https://github.com/openclaw/openclaw")
        {
            throw new InvalidDataException(
                "Payload repository does not match openclaw/openclaw.");
        }

        if (ResolvedCommit.Length != 40 || !ResolvedCommit.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Payload source commit must be a full Git SHA.");
        }

        if (Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Payload SHA-256 is invalid.");
        }

        if (Architecture is not ("x64" or "arm64"))
        {
            throw new InvalidDataException("Payload architecture is unsupported.");
        }

        if (string.IsNullOrWhiteSpace(Archive) ||
            Path.GetFileName(Archive) != Archive)
        {
            throw new InvalidDataException("Payload archive name is invalid.");
        }
    }

    internal static void ValidateForCurrentProcess(
        PayloadMetadata metadata,
        string payloadPath)
    {
        string processArchitecture = RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException(
                $"Unsupported process architecture: {RuntimeInformation.ProcessArchitecture}.")
        };
        if (!string.Equals(
            metadata.Architecture,
            processArchitecture,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Payload architecture does not match the host process.");
        }

        if (!string.Equals(
            metadata.Archive,
            Path.GetFileName(payloadPath),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Payload file name does not match its metadata.");
        }
    }
}
