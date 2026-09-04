namespace OpenClaw.Launcher.Tests;

public sealed class GatewayStopperTests : IDisposable
{
    private readonly string _payloadDirectory = TestDirectory.Create();

    public GatewayStopperTests()
    {
        File.WriteAllText(
            Path.Combine(_payloadDirectory, "openclaw.mjs"),
            "console.log('fixture');");
    }

    [Fact]
    public void CreateStartInfoUsesThePreparedGatewayStopCommand()
    {
        var startInfo = GatewayStopper.CreateStartInfo(
            "node",
            _payloadDirectory);

        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.Equal(_payloadDirectory, startInfo.WorkingDirectory);
        Assert.Equal("1", startInfo.Environment["OPENCLAW_NO_AUTO_UPDATE"]);
        Assert.Equal(
            [
                Path.Combine(_payloadDirectory, "openclaw.mjs"),
                "gateway",
                "stop",
                "--json"
            ],
            startInfo.ArgumentList);
    }

    public void Dispose()
    {
        Directory.Delete(_payloadDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
