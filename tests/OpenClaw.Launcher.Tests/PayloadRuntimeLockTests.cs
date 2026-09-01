namespace OpenClaw.Launcher.Tests;

public sealed class PayloadRuntimeLockTests : IDisposable
{
    private readonly string _testDirectory = TestDirectory.Create();

    [Fact]
    public void MultipleLaunchesCanShareTheRuntimeLock()
    {
        string installDirectory = Path.Combine(_testDirectory, "app");

        using FileStream first =
            PayloadRuntimeLock.AcquireForLaunch(installDirectory);
        using FileStream second =
            PayloadRuntimeLock.AcquireForLaunch(installDirectory);

        Assert.True(first.CanRead);
        Assert.True(second.CanRead);
    }

    [Fact]
    public void MutationIsRejectedWhileOpenClawUsesThePayload()
    {
        string installDirectory = Path.Combine(_testDirectory, "app");
        using FileStream launch =
            PayloadRuntimeLock.AcquireForLaunch(installDirectory);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => PayloadRuntimeLock.AcquireForMutation(installDirectory));

        Assert.Contains(
            "currently using",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LaunchIsRejectedWhileClawCtlMutatesThePayload()
    {
        string installDirectory = Path.Combine(_testDirectory, "app");
        using FileStream mutation =
            PayloadRuntimeLock.AcquireForMutation(installDirectory);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => PayloadRuntimeLock.AcquireForLaunch(installDirectory));

        Assert.Contains(
            "being prepared or repaired",
            exception.Message,
            StringComparison.Ordinal);
    }

    public void Dispose()
    {
        Directory.Delete(_testDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
