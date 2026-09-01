namespace OpenClaw.Launcher.Tests;

public sealed class ClawCtlConsoleTests : IDisposable
{
    private readonly string _testDirectory = TestDirectory.Create();

    [Fact]
    public void WriteHelpListsOnlyThePublicCommands()
    {
        var output = new StringWriter();

        ClawCtlConsole.WriteHelp(output);

        string help = output.ToString();
        Assert.Contains("prepare", help, StringComparison.Ordinal);
        Assert.Contains("verify", help, StringComparison.Ordinal);
        Assert.Contains("repair", help, StringComparison.Ordinal);
        Assert.Contains(
            NodeRuntimeResolver.SupportedVersions,
            help,
            StringComparison.Ordinal);
        Assert.Contains(
            NodeRuntimeResolver.InstallCommand,
            help,
            StringComparison.Ordinal);
        Assert.DoesNotContain("update-package", help, StringComparison.Ordinal);
        Assert.DoesNotContain("gateway-service", help, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, "current and was reused")]
    [InlineData(false, false, "verified and prepared")]
    [InlineData(true, true, "passed full verification")]
    [InlineData(false, true, "recreated from packaged content")]
    public void WritePreparationSummaryDescribesResult(
        bool reused,
        bool repair,
        string expectedStatus)
    {
        var output = new StringWriter();

        ClawCtlConsole.WritePreparationSummary(
            output,
            new StagedPayload(
                Path.Combine(_testDirectory, "app"),
                new string('a', 64),
                reused),
            repair);

        Assert.Contains(
            expectedStatus,
            output.ToString(),
            StringComparison.Ordinal);
    }

    public void Dispose()
    {
        Directory.Delete(_testDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
