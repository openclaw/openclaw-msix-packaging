using System.Diagnostics;

namespace OpenClaw.Launcher;

public static class GatewayLauncher
{
    public static async Task<int> RunAsync(
        string nodePath,
        string payloadDirectory,
        IReadOnlyList<string> openClawArguments,
        CancellationToken cancellationToken,
        Action<string>? log = null)
    {
        ProcessStartInfo startInfo = CreateStartInfo(
            nodePath,
            payloadDirectory,
            openClawArguments);
        log?.Invoke("Launching OpenClaw with forwarded command arguments.");
        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Unable to start the OpenClaw process.");
        log?.Invoke($"OpenClaw child process started with PID {process.Id}.");

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            log?.Invoke($"OpenClaw child process exited with code {process.ExitCode}.");
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            throw;
        }
    }

    public static ProcessStartInfo CreateStartInfo(
        string nodePath,
        string payloadDirectory,
        IReadOnlyList<string> openClawArguments)
    {
        string entryPoint = Path.Combine(payloadDirectory, "openclaw.mjs");
        if (!File.Exists(entryPoint))
        {
            throw new FileNotFoundException(
                "The staged OpenClaw entry point was not found.",
                entryPoint);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = nodePath,
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        startInfo.Environment["OPENCLAW_SUPERVISOR_MODE"] = "external";
        startInfo.Environment["OPENCLAW_SERVICE_REPAIR_POLICY"] = "external";
        startInfo.Environment["OPENCLAW_NO_AUTO_UPDATE"] = "1";
        startInfo.ArgumentList.Add(entryPoint);

        foreach (string argument in openClawArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
