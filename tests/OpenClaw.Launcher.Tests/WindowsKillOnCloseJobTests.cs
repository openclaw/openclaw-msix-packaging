using System.Diagnostics;

namespace OpenClaw.Launcher.Tests;

public sealed class WindowsKillOnCloseJobTests
{
    [Fact]
    public async Task DisposingJobTerminatesAssignedProcess()
    {
        using WindowsKillOnCloseJob job = WindowsKillOnCloseJob.Create();
        using Process process = job.StartProcess(new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.SystemDirectory,
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"),
            UseShellExecute = false,
            ArgumentList =
            {
                "-NoLogo",
                "-NoProfile",
                "-Command",
                "Start-Sleep -Seconds 60"
            }
        });

        Assert.False(process.HasExited);

        job.Dispose();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(timeout.Token);

        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task FastExitRetainsExitCodeAfterDelayedObservation()
    {
        using WindowsKillOnCloseJob job = WindowsKillOnCloseJob.Create();
        using Process process = job.StartProcess(new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.SystemDirectory,
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"),
            UseShellExecute = false,
            ArgumentList =
            {
                "-NoLogo",
                "-NoProfile",
                "-Command",
                "exit 37"
            }
        });

        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await process.WaitForExitAsync();

        Assert.Equal(37, process.ExitCode);
    }
}
