using System.Diagnostics;
using System.Globalization;

namespace OpenClaw.Launcher;

internal static class PayloadProcessRegistry
{
    private const string PendingState = "pending";
    private const string RunningState = "running";
    private const string RecordPattern = "*.process";

    public static PayloadProcessRegistration Reserve(string installDirectory)
    {
        string registryDirectory = GetRegistryDirectory(installDirectory);
        Directory.CreateDirectory(registryDirectory);

        using Process owner = Process.GetCurrentProcess();
        var registration = new PayloadProcessRegistration(
            Path.Combine(
                registryDirectory,
                $"{owner.Id}-{Guid.NewGuid():N}.process"),
            owner.Id,
            owner.StartTime.ToUniversalTime().Ticks);
        registration.WritePendingRecord();
        return registration;
    }

    public static async Task StopTrackedProcessesAsync(
        string installDirectory,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(log);

        string registryDirectory = GetRegistryDirectory(installDirectory);
        if (!Directory.Exists(registryDirectory))
        {
            return;
        }

        foreach (string recordPath in Directory
                     .EnumerateFiles(registryDirectory, RecordPattern)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            PayloadProcessRecord record = ReadRecord(recordPath);

            if (record.State == PendingState)
            {
                if (IsSameProcess(
                    record.OwnerProcessId,
                    record.OwnerStartTimeUtcTicks))
                {
                    throw new InvalidOperationException(
                        "A packaged OpenClaw process is still starting. " +
                        "Stop OpenClaw before preparing the payload.");
                }

                if (File.GetLastWriteTimeUtc(recordPath) < GetSystemBootTimeUtc())
                {
                    File.Delete(recordPath);
                    continue;
                }

                throw new InvalidOperationException(
                    "A packaged OpenClaw launch ended before its child " +
                    "process could be recorded. Restart Windows before " +
                    "preparing the payload.");
            }

            Process? process = TryOpenMatchingProcess(
                record.ProcessId,
                record.StartTimeUtcTicks);
            if (process is null)
            {
                File.Delete(recordPath);
                continue;
            }

            using (process)
            {
                IReadOnlyList<Process> descendants =
                    WindowsProcessTree.OpenDescendants(process.Id);
                try
                {
                    log(
                        $"Stopping packaged OpenClaw process {record.ProcessId} " +
                        "before replacing the prepared payload.");
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken);
                    foreach (Process descendant in descendants)
                    {
                        await descendant.WaitForExitAsync(cancellationToken);
                    }

                    log($"Stopped packaged OpenClaw process {record.ProcessId}.");
                }
                finally
                {
                    foreach (Process descendant in descendants)
                    {
                        descendant.Dispose();
                    }
                }
            }

            File.Delete(recordPath);
        }
    }

    internal static string GetRegistryDirectory(string installDirectory)
    {
        string fullInstallDirectory = Path.GetFullPath(installDirectory);
        string? installRoot = Path.GetDirectoryName(fullInstallDirectory);
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            throw new InvalidOperationException(
                "The install directory must have a parent directory.");
        }

        return Path.Combine(
            installRoot,
            $".{Path.GetFileName(fullInstallDirectory)}.runtime-users");
    }

    internal static void WriteRecord(
        string recordPath,
        IReadOnlyList<string> lines)
    {
        string temporaryPath = recordPath + $".{Guid.NewGuid():N}.partial";
        try
        {
            File.WriteAllLines(temporaryPath, lines);
            File.Move(temporaryPath, recordPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static PayloadProcessRecord ReadRecord(string recordPath)
    {
        string[] lines = File.ReadAllLines(recordPath);
        if (lines.Length < 3 ||
            !TryReadPositiveInt(lines[1], out int ownerProcessId) ||
            !TryReadPositiveLong(lines[2], out long ownerStartTimeUtcTicks))
        {
            throw InvalidRecord(recordPath);
        }

        if (string.Equals(lines[0], PendingState, StringComparison.Ordinal) &&
            lines.Length == 3)
        {
            return new PayloadProcessRecord(
                PendingState,
                ownerProcessId,
                ownerStartTimeUtcTicks,
                ProcessId: 0,
                StartTimeUtcTicks: 0);
        }

        if (string.Equals(lines[0], RunningState, StringComparison.Ordinal) &&
            lines.Length == 5 &&
            TryReadPositiveInt(lines[3], out int processId) &&
            TryReadPositiveLong(lines[4], out long startTimeUtcTicks))
        {
            return new PayloadProcessRecord(
                RunningState,
                ownerProcessId,
                ownerStartTimeUtcTicks,
                processId,
                startTimeUtcTicks);
        }

        throw InvalidRecord(recordPath);
    }

    private static bool IsSameProcess(int processId, long startTimeUtcTicks)
    {
        using Process? process = TryOpenMatchingProcess(
            processId,
            startTimeUtcTicks);
        return process is not null;
    }

    private static DateTime GetSystemBootTimeUtc() =>
        DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

    private static Process? TryOpenMatchingProcess(
        int processId,
        long startTimeUtcTicks)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return null;
        }

        try
        {
            process.Refresh();
            if (process.HasExited ||
                process.StartTime.ToUniversalTime().Ticks != startTimeUtcTicks)
            {
                process.Dispose();
                return null;
            }

            return process;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private static bool TryReadPositiveInt(string value, out int parsed) =>
        int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out parsed) &&
        parsed > 0;

    private static bool TryReadPositiveLong(string value, out long parsed) =>
        long.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out parsed) &&
        parsed > 0;

    private static InvalidDataException InvalidRecord(string recordPath) =>
        new($"The packaged OpenClaw process record is invalid: {recordPath}");

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record PayloadProcessRecord(
        string State,
        int OwnerProcessId,
        long OwnerStartTimeUtcTicks,
        int ProcessId,
        long StartTimeUtcTicks);
}

internal sealed class PayloadProcessRegistration(
    string recordPath,
    int ownerProcessId,
    long ownerStartTimeUtcTicks) : IDisposable
{
    private string? _recordPath = recordPath;

    public void Attach(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        string path = _recordPath ??
            throw new ObjectDisposedException(nameof(PayloadProcessRegistration));

        process.Refresh();
        PayloadProcessRegistry.WriteRecord(
            path,
            [
                "running",
                ownerProcessId.ToString(CultureInfo.InvariantCulture),
                ownerStartTimeUtcTicks.ToString(CultureInfo.InvariantCulture),
                process.Id.ToString(CultureInfo.InvariantCulture),
                process.StartTime.ToUniversalTime().Ticks.ToString(
                    CultureInfo.InvariantCulture)
            ]);
    }

    internal void WritePendingRecord()
    {
        string path = _recordPath ??
            throw new ObjectDisposedException(nameof(PayloadProcessRegistration));
        PayloadProcessRegistry.WriteRecord(
            path,
            [
                "pending",
                ownerProcessId.ToString(CultureInfo.InvariantCulture),
                ownerStartTimeUtcTicks.ToString(CultureInfo.InvariantCulture)
            ]);
    }

    public void Dispose()
    {
        string? path = Interlocked.Exchange(ref _recordPath, null);
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
