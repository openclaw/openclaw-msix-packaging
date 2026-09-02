using System.Runtime.InteropServices;
using System.Text.Json;

namespace OpenClaw.Launcher.Tests;

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
            """
            {
              "PayloadSha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
              "Files": [
                {
                  "Path": "openclaw.mjs",
                  "Length": 7,
                  "Sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                }
              ]
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(options.InstallDirectory, ".payload-verified-sha256"),
            new string('b', 64));

        string resolved = await PreparedPayloadResolver.ResolveAsync(
            options,
            CancellationToken.None);

        Assert.Equal(options.InstallDirectory, resolved);
    }

    [Fact]
    public async Task ResolveAsyncDirectsTheUserToClawCtlWhenListedFileIsMissing()
    {
        HostOptions options = await CreateOptionsAsync();
        Directory.CreateDirectory(options.InstallDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(options.InstallDirectory, "openclaw.mjs"),
            "fixture");
        await File.WriteAllTextAsync(
            Path.Combine(options.InstallDirectory, "required-module.mjs"),
            "fixture");
        await File.WriteAllTextAsync(
            Path.Combine(options.InstallDirectory, ".payload-inventory.json"),
            """
            {
              "PayloadSha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
              "Files": [
                {
                  "Path": "openclaw.mjs",
                  "Length": 7,
                  "Sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                },
                {
                  "Path": "required-module.mjs",
                  "Length": 7,
                  "Sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                }
              ]
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(options.InstallDirectory, ".payload-verified-sha256"),
            new string('b', 64));
        File.Delete(Path.Combine(
            options.InstallDirectory,
            "required-module.mjs"));

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => PreparedPayloadResolver.ResolveAsync(
                    options,
                    CancellationToken.None));

        Assert.Contains(
            "not prepared or is incomplete",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "clawctl prepare",
            exception.Message,
            StringComparison.Ordinal);
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
            "not prepared or is incomplete",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "clawctl prepare",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsyncDirectsTheUserToClawCtlWhenPayloadIsOutdated()
    {
        HostOptions options = await CreateOptionsAsync();
        Directory.CreateDirectory(options.InstallDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(options.InstallDirectory, "openclaw.mjs"),
            "fixture");
        await File.WriteAllTextAsync(
            Path.Combine(
                options.InstallDirectory,
                PayloadStager.InventoryFileName),
            """
            {
              "PayloadSha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
              "Files": [
                {
                  "Path": "openclaw.mjs",
                  "Length": 7,
                  "Sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                }
              ]
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(
                options.InstallDirectory,
                PayloadStager.VerificationMarkerFileName),
            new string('c', 64));

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => PreparedPayloadResolver.ResolveAsync(
                    options,
                    CancellationToken.None));

        Assert.Contains(
            "out of date for the installed package",
            exception.Message,
            StringComparison.Ordinal);
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
            Path.Combine(_testDirectory, "app"),
            []);
    }

    public void Dispose()
    {
        Directory.Delete(_testDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
