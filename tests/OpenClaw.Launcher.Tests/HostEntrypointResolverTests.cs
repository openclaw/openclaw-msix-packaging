namespace OpenClaw.Launcher.Tests;

public sealed class HostEntrypointResolverTests
{
    [Fact]
    public void ControlAliasSelectsTheManagementEntrypoint() =>
        Assert.Equal(
            HostEntrypoint.Control,
            HostEntrypointResolver.Resolve(
                "\"C:\\Users\\someone\\AppData\\Local\\Microsoft\\WindowsApps\\clawctl.exe\" setup"));

    [Fact]
    public void AgentAliasSelectsThePassthroughEntrypoint() =>
        Assert.Equal(
            HostEntrypoint.Agent,
            HostEntrypointResolver.Resolve(
                "\"C:\\Users\\someone\\AppData\\Local\\Microsoft\\WindowsApps\\openclaw.exe\" setup"));

    [Fact]
    public void UnknownOrMalformedInvocationDefaultsToAgent()
    {
        Assert.Equal(
            HostEntrypoint.Agent,
            HostEntrypointResolver.Resolve("launcher.exe"));
        Assert.Equal(
            HostEntrypoint.Agent,
            HostEntrypointResolver.Resolve("\"C:\\x\\clawctl.exe setup"));
    }

    [Fact]
    public void OnlyArgvZeroSelectsTheEntrypoint() =>
        Assert.Equal(
            HostEntrypoint.Agent,
            HostEntrypointResolver.Resolve(
                "openclaw.exe run clawctl.exe repair"));
}
