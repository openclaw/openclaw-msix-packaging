namespace OpenClaw.WindowsLauncher;

internal static class PayloadRuntimeLock
{
    public static FileStream AcquireForLaunch(string installDirectory)
    {
        try
        {
            return Open(
                installDirectory,
                FileAccess.ReadWrite,
                FileShare.ReadWrite);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                "The packaged OpenClaw payload is being prepared or repaired. " +
                "Wait for clawctl to finish, then retry.",
                exception);
        }
    }

    public static FileStream AcquireForVerification(string installDirectory)
    {
        try
        {
            return Open(
                installDirectory,
                FileAccess.ReadWrite,
                FileShare.ReadWrite);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                "The prepared OpenClaw payload is being prepared or repaired. " +
                "Wait for clawctl to finish, then retry.",
                exception);
        }
    }

    public static FileStream AcquireForMutation(string installDirectory)
    {
        try
        {
            return Open(
                installDirectory,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                "OpenClaw is currently using the prepared payload. " +
                "Stop the packaged OpenClaw process before preparing or repairing it.",
                exception);
        }
    }

    private static FileStream Open(
        string installDirectory,
        FileAccess access,
        FileShare share)
    {
        string? installRoot = Path.GetDirectoryName(installDirectory);
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            throw new InvalidOperationException(
                "The install directory must have a parent directory.");
        }

        Directory.CreateDirectory(installRoot);
        string lockPath = Path.Combine(
            installRoot,
            $".{Path.GetFileName(installDirectory)}.runtime.lock");
        return new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            access,
            share,
            bufferSize: 1,
            FileOptions.None);
    }
}
