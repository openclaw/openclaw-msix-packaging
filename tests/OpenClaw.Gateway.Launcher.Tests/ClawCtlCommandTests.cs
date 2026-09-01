namespace OpenClaw.WindowsLauncher.Tests;

public sealed class ClawCtlCommandTests
{
    [Theory]
    [InlineData(ClawCtlCommand.Help)]
    [InlineData(ClawCtlCommand.Prepare, "prepare")]
    [InlineData(ClawCtlCommand.Verify, "verify")]
    [InlineData(ClawCtlCommand.Repair, "repair")]
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
    [InlineData("setup")]
    [InlineData("doctor")]
    [InlineData("gateway", "status")]
    [InlineData("update-package")]
    [InlineData("prepare", "--force")]
    public void ParseRejectsCommandsOutsideThePublicManagementSurface(
        params string[] args)
    {
        ClawCtlCommandParseResult result = ClawCtlCommandParser.Parse(args);

        Assert.NotNull(result.Error);
    }
}
