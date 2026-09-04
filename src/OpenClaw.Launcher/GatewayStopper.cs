using System.Diagnostics;

namespace OpenClaw.Launcher;

internal static class GatewayStopper
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

    public static async Task StopAsync(
        string nodePath,
        string payloadDirectory,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(log);

        ProcessStartInfo startInfo = CreateStartInfo(
            nodePath,
            payloadDirectory);
        log("Requesting the existing OpenClaw gateway to stop.");
        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                "Unable to start the OpenClaw gateway stop command.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(StopTimeout);
        Task<string> standardOutputTask =
            process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardErrorTask =
            process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw new TimeoutException(
                "The existing OpenClaw gateway did not stop within 30 seconds.");
        }

        string standardOutput = await standardOutputTask;
        string standardError = await standardErrorTask;
        if (process.ExitCode != 0)
        {
            string? detail = FirstNonEmpty(standardError, standardOutput);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(detail)
                    ? "The existing OpenClaw gateway could not be stopped."
                    : $"The existing OpenClaw gateway could not be stopped: " +
                      detail.Trim());
        }

        log("The existing OpenClaw gateway stop command completed.");
    }

    internal static ProcessStartInfo CreateStartInfo(
        string nodePath,
        string payloadDirectory)
    {
        string entryPoint = Path.Combine(payloadDirectory, "openclaw.mjs");
        if (!File.Exists(entryPoint))
        {
            throw new FileNotFoundException(
                "The prepared OpenClaw entry point was not found.",
                entryPoint);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = nodePath,
            WorkingDirectory = payloadDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["OPENCLAW_NO_AUTO_UPDATE"] = "1";
        startInfo.ArgumentList.Add(entryPoint);
        startInfo.ArgumentList.Add("gateway");
        startInfo.ArgumentList.Add("stop");
        startInfo.ArgumentList.Add("--json");
        return startInfo;
    }

    private static string? FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
