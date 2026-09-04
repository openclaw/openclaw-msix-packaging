using System.Diagnostics;
using System.Globalization;

namespace OpenClaw.Launcher.Tests;

public sealed class PayloadProcessRegistryTests : IDisposable
{
    private readonly string _testDirectory = TestDirectory.Create();

    [Fact]
    public void RegistrationIsRemovedWhenTheLauncherReleasesIt()
    {
        string installDirectory = Path.Combine(_testDirectory, "app");
        using Process process = TestProcess.StartLongRunning();

        using (PayloadProcessRegistration registration =
               PayloadProcessRegistry.Reserve(installDirectory))
        {
            registration.Attach(process);
            Assert.Single(Directory.GetFiles(
                PayloadProcessRegistry.GetRegistryDirectory(installDirectory),
                "*.process"));
        }

        Assert.Empty(Directory.GetFiles(
            PayloadProcessRegistry.GetRegistryDirectory(installDirectory),
            "*.process"));
        TestProcess.Stop(process);
    }

    [Fact]
    public async Task StopTrackedProcessesStopsTheRecordedProcessTree()
    {
        string installDirectory = Path.Combine(_testDirectory, "app");
        var messages = new List<string>();
        using Process process = TestProcess.StartLongRunning();
        using PayloadProcessRegistration registration =
            PayloadProcessRegistry.Reserve(installDirectory);
        registration.Attach(process);

        await PayloadProcessRegistry.StopTrackedProcessesAsync(
            installDirectory,
            messages.Add,
            CancellationToken.None);

        process.Refresh();
        Assert.True(process.HasExited);
        Assert.Contains(
            messages,
            message => message.Contains(
                $"process {process.Id}",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task StopTrackedProcessesDoesNotKillAReusedProcessId()
    {
        string installDirectory = Path.Combine(_testDirectory, "app");
        using Process process = TestProcess.StartLongRunning();
        using PayloadProcessRegistration registration =
            PayloadProcessRegistry.Reserve(installDirectory);
        registration.Attach(process);
        string recordPath = Assert.Single(Directory.GetFiles(
            PayloadProcessRegistry.GetRegistryDirectory(installDirectory),
            "*.process"));
        string[] lines = File.ReadAllLines(recordPath);
        lines[4] = (long.Parse(
            lines[4],
            CultureInfo.InvariantCulture) + 1).ToString(
                CultureInfo.InvariantCulture);
        File.WriteAllLines(recordPath, lines);

        await PayloadProcessRegistry.StopTrackedProcessesAsync(
            installDirectory,
            _ => { },
            CancellationToken.None);

        process.Refresh();
        Assert.False(process.HasExited);
        TestProcess.Stop(process);
    }

    [Fact]
    public async Task PendingLaunchRecordBlocksPayloadReplacement()
    {
        string installDirectory = Path.Combine(_testDirectory, "app");
        using PayloadProcessRegistration registration =
            PayloadProcessRegistry.Reserve(installDirectory);

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => PayloadProcessRegistry.StopTrackedProcessesAsync(
                    installDirectory,
                    _ => { },
                    CancellationToken.None));

        Assert.Contains("still starting", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PendingLaunchRecordFromBeforeBootIsRemoved()
    {
        string installDirectory = Path.Combine(_testDirectory, "app");
        string registryDirectory =
            PayloadProcessRegistry.GetRegistryDirectory(installDirectory);
        Directory.CreateDirectory(registryDirectory);
        string recordPath = Path.Combine(registryDirectory, "stale.process");
        PayloadProcessRegistry.WriteRecord(
            recordPath,
            [
                "pending",
                int.MaxValue.ToString(CultureInfo.InvariantCulture),
                DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)
            ]);
        File.SetLastWriteTimeUtc(
            recordPath,
            DateTime.UtcNow -
            TimeSpan.FromMilliseconds(Environment.TickCount64) -
            TimeSpan.FromMinutes(1));

        await PayloadProcessRegistry.StopTrackedProcessesAsync(
            installDirectory,
            _ => { },
            CancellationToken.None);

        Assert.False(File.Exists(recordPath));
    }

    public void Dispose()
    {
        Directory.Delete(_testDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
