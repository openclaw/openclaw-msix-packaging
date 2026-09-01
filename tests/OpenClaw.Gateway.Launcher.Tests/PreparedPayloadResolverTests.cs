using System.Runtime.InteropServices;
using System.Text.Json;

namespace OpenClaw.WindowsLauncher.Tests;

public sealed class PreparedPayloadResolverTests : IDisposable
{
    private readonly string _testDirectory = TestDirectory.Create();

    [Fact]
    public async Task ResolveAsyncReturnsPreparedDirectoryForMatchingMarker()
    {
        HostOptions options = await CreateOptionsAsync();
        Directory.CreateDirectory(options.InstallDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(options.InstallDirectory, "openclaw.mjs"),
            "fixture");
        await File.WriteAllTextAsync(
            Path.Combine(options.InstallDirectory, ".payload-inventory.json"),
            "{}");
        await File.WriteAllTextAsync(
            Path.Combine(options.InstallDirectory, ".payload-verified-sha256"),
            new string('b', 64));

        string resolved = await PreparedPayloadResolver.ResolveAsync(
            options,
            CancellationToken.None);

        Assert.Equal(options.InstallDirectory, resolved);
    }

    [Fact]
    public async Task ResolveAsyncDirectsTheUserToClawCtlWhenNotPrepared()
    {
        HostOptions options = await CreateOptionsAsync();

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => PreparedPayloadResolver.ResolveAsync(
                    options,
                    CancellationToken.None));

        Assert.Contains(
            "clawctl prepare",
            exception.Message,
            StringComparison.Ordinal);
    }

    private async Task<HostOptions> CreateOptionsAsync()
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
            "node",
            Path.Combine(_testDirectory, "app"),
            []);
    }

    public void Dispose()
    {
        Directory.Delete(_testDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
