using System.Runtime.InteropServices;
using System.Text.Json;

namespace OpenClaw.Launcher.Tests;

public sealed class ProgramTests : IDisposable
{
    private readonly string _testDirectory = TestDirectory.Create();

    [Fact]
    public async Task AgentLaunchChecksPreparedStateBeforeNodeDependency()
    {
        HostOptions options = await CreateUnpreparedOptionsAsync();
        bool nodeResolutionAttempted = false;

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Program.RunAgentAsync(
                    options,
                    _ => { },
                    _ =>
                    {
                        nodeResolutionAttempted = true;
                        return Task.FromException<NodeRuntime>(
                            new InvalidOperationException("Node resolution should not run."));
                    }));

        Assert.False(nodeResolutionAttempted);
        Assert.Contains("clawctl prepare", exception.Message);
    }

    private async Task<HostOptions> CreateUnpreparedOptionsAsync()
    {
        string architecture = RuntimeInformation.ProcessArchitecture ==
            Architecture.Arm64
                ? "arm64"
                : "x64";
        string payloadPath = Path.Combine(
            _testDirectory,
            $"app-{architecture}.tar.gz");
        await File.WriteAllTextAsync(payloadPath, "payload");
        string metadataPath = Path.Combine(
            _testDirectory,
            "payload-metadata.json");
        await File.WriteAllTextAsync(
            metadataPath,
            JsonSerializer.Serialize(new
            {
                repository = "https://github.com/openclaw/openclaw",
                resolvedCommit = new string('a', 40),
                architecture,
                archive = Path.GetFileName(payloadPath),
                sha256 = new string('b', 64)
            }));

        return new HostOptions(
            payloadPath,
            metadataPath,
            Path.Combine(_testDirectory, "app"),
            []);
    }

    public void Dispose()
    {
        Directory.Delete(_testDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
