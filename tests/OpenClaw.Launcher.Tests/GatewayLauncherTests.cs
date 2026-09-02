namespace OpenClaw.Launcher.Tests;

public sealed class GatewayLauncherTests : IDisposable
{
    private readonly string _payloadDirectory = TestDirectory.Create();

    public GatewayLauncherTests()
    {
        File.WriteAllText(
            Path.Combine(_payloadDirectory, "openclaw.mjs"),
            "console.log('fixture');");
    }

    [Fact]
    public void CreateStartInfoForwardsNoArgumentsUnchanged()
    {
        var startInfo = GatewayLauncher.CreateStartInfo(
            "node",
            _payloadDirectory,
            []);

        Assert.False(startInfo.UseShellExecute);
        Assert.False(startInfo.RedirectStandardError);
        Assert.Empty(startInfo.WorkingDirectory);
        Assert.Equal(
            "external",
            startInfo.Environment["OPENCLAW_SUPERVISOR_MODE"]);
        Assert.Equal(
            "external",
            startInfo.Environment["OPENCLAW_SERVICE_REPAIR_POLICY"]);
        Assert.Equal("1", startInfo.Environment["OPENCLAW_NO_AUTO_UPDATE"]);
        Assert.Equal(
            [Path.Combine(_payloadDirectory, "openclaw.mjs")],
            startInfo.ArgumentList);
    }

    [Fact]
    public void CreateStartInfoPreservesExplicitArguments()
    {
        string[] arguments = ["status", "--json", "value with spaces"];

        var startInfo = GatewayLauncher.CreateStartInfo(
            "node",
            _payloadDirectory,
            arguments);

        Assert.False(startInfo.RedirectStandardError);
        Assert.Empty(startInfo.WorkingDirectory);
        Assert.Equal(
            "external",
            startInfo.Environment["OPENCLAW_SUPERVISOR_MODE"]);
        Assert.Equal(
            "external",
            startInfo.Environment["OPENCLAW_SERVICE_REPAIR_POLICY"]);
        Assert.Equal("1", startInfo.Environment["OPENCLAW_NO_AUTO_UPDATE"]);
        Assert.Equal(
            [Path.Combine(_payloadDirectory, "openclaw.mjs"), .. arguments],
            startInfo.ArgumentList);
    }

    [Theory]
    [InlineData("update", "--yes")]
    [InlineData("--update")]
    [InlineData("gateway", "call", "update.run")]
    [InlineData("gateway", "install")]
    [InlineData("setup", "--install-daemon")]
    [InlineData("onboard", "--mode", "local")]
    public void CreateStartInfoForwardsCommandsWithoutInterpretation(
        params string[] arguments)
    {
        var startInfo = GatewayLauncher.CreateStartInfo(
            "node",
            _payloadDirectory,
            arguments);

        Assert.Equal(
            [Path.Combine(_payloadDirectory, "openclaw.mjs"), .. arguments],
            startInfo.ArgumentList);
    }

    public void Dispose()
    {
        Directory.Delete(_payloadDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
