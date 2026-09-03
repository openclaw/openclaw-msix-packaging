namespace OpenClaw.Launcher.Tests;

public sealed class ClawCtlCommandTests
{
    [Theory]
    [InlineData(ClawCtlCommand.Help)]
    [InlineData(ClawCtlCommand.Setup, "setup")]
    [InlineData(ClawCtlCommand.Version, "--version")]
    public void ParseAcceptsOnlyThePublicManagementSurface(
        ClawCtlCommand expected,
        params string[] args)
    {
        ClawCtlCommandParseResult result = ClawCtlCommandParser.Parse(args);

        Assert.Null(result.Error);
        Assert.Equal(expected, result.Command);
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("doctor")]
    [InlineData("gateway", "status")]
    [InlineData("update-package")]
    [InlineData("verify")]
    [InlineData("repair")]
    [InlineData("setup", "--force")]
    public void ParseRejectsCommandsOutsideThePublicManagementSurface(
        params string[] args)
    {
        ClawCtlCommandParseResult result = ClawCtlCommandParser.Parse(args);

        Assert.NotNull(result.Error);
    }
}
