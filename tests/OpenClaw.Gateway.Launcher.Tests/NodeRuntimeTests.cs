using System.Runtime.InteropServices;

namespace OpenClaw.Gateway.Launcher.Tests;

public sealed class NodeRuntimeTests
{
    [Fact]
    public async Task ResolverUsesCompatibleExistingRuntime()
    {
        int installCount = 0;
        var resolver = CreateResolver(
            () => ["node.exe"],
            (_, _) => Task.FromResult<NodeRuntimeInfo?>(
                Runtime("node.exe", new Version(24, 16, 0))),
            _ =>
            {
                installCount++;
                return Task.CompletedTask;
            });

        NodeRuntimeInfo runtime = await resolver.ResolveAsync(
            CancellationToken.None);

        Assert.Equal(new Version(24, 16, 0), runtime.Version);
        Assert.Equal(0, installCount);
    }

    [Fact]
    public async Task ResolverInstallsAndReprobesWhenRuntimeIsTooOld()
    {
        int findCount = 0;
        int installCount = 0;
        var resolver = CreateResolver(
            () =>
            {
                findCount++;
                return findCount == 1 ? ["old-node.exe"] : ["new-node.exe"];
            },
            (path, _) => Task.FromResult<NodeRuntimeInfo?>(
                path.Contains("old", StringComparison.Ordinal)
                    ? Runtime(path, new Version(22, 0, 0))
                    : Runtime(path, new Version(24, 17, 0))),
            _ =>
            {
                installCount++;
                return Task.CompletedTask;
            });

        NodeRuntimeInfo runtime = await resolver.ResolveAsync(
            CancellationToken.None);

        Assert.Equal(new Version(24, 17, 0), runtime.Version);
        Assert.Equal(1, installCount);
        Assert.Equal(2, findCount);
    }

    [Fact]
    public async Task ResolverRejectsWrongArchitectureAfterInstall()
    {
        var resolver = CreateResolver(
            () => ["node.exe"],
            (_, _) => Task.FromResult<NodeRuntimeInfo?>(
                Runtime(
                    "node.exe",
                    new Version(24, 16, 0),
                    Architecture.Arm64)),
            _ => Task.CompletedTask,
            Architecture.X64);

        InvalidOperationException exception = await Assert.ThrowsAsync<
            InvalidOperationException>(
            () => resolver.ResolveAsync(CancellationToken.None));

        Assert.Contains(
            "compatible Node.js runtime could not be found",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("24.16.0|x64", Architecture.X64)]
    [InlineData("24.16.1|arm64", Architecture.Arm64)]
    public void ProbeOutputParsesVersionAndArchitecture(
        string output,
        Architecture architecture)
    {
        NodeRuntimeInfo? runtime = SystemNodeRuntime.TryParseProbeOutput(
            Path.GetFullPath("node.exe"),
            output);

        Assert.NotNull(runtime);
        Assert.Equal(architecture, runtime.Architecture);
        Assert.Equal(24, runtime.Version.Major);
    }

    [Theory]
    [InlineData("")]
    [InlineData("24.16.0")]
    [InlineData("not-a-version|x64")]
    [InlineData("24.16.0|ia32")]
    public void ProbeOutputRejectsInvalidValues(string output)
    {
        Assert.Null(SystemNodeRuntime.TryParseProbeOutput("node.exe", output));
    }

    [Fact]
    public void WinGetCommandUsesOfficialExactPackage()
    {
        var startInfo = WinGetNodeInstaller.CreateStartInfo("winget.exe");

        Assert.Equal("winget.exe", startInfo.FileName);
        Assert.Equal(
            [
                "install",
                "--id", "OpenJS.NodeJS.LTS",
                "--exact",
                "--source", "winget",
                "--accept-package-agreements",
                "--accept-source-agreements",
                "--silent"
            ],
            startInfo.ArgumentList);
    }

    private static NodeRuntimeResolver CreateResolver(
        Func<IReadOnlyList<string>> findCandidates,
        Func<string, CancellationToken, Task<NodeRuntimeInfo?>> probe,
        Func<CancellationToken, Task> install,
        Architecture architecture = Architecture.X64) =>
        new(
            findCandidates,
            probe,
            install,
            architecture);

    private static NodeRuntimeInfo Runtime(
        string path,
        Version version,
        Architecture architecture = Architecture.X64) =>
        new(Path.GetFullPath(path), version, architecture);
}
