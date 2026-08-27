using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenClaw.Gateway.Launcher;

internal sealed record NodeRuntimeInfo(
    string ExecutablePath,
    Version Version,
    Architecture Architecture);

internal sealed class NodeRuntimeResolver(
    Func<IReadOnlyList<string>> findCandidates,
    Func<string, CancellationToken, Task<NodeRuntimeInfo?>> probe,
    Func<CancellationToken, Task> install,
    Architecture requiredArchitecture,
    Action<string>? log = null)
{
    public static Version MinimumVersion { get; } = new(24, 16, 0);

    public async Task<NodeRuntimeInfo> ResolveAsync(
        CancellationToken cancellationToken)
    {
        NodeRuntimeInfo? runtime = await FindCompatibleAsync(cancellationToken);
        if (runtime is not null)
        {
            return runtime;
        }

        log?.Invoke(
            $"Compatible Node.js {MinimumVersion}+ was not found. " +
            "Installing the official OpenJS Node.js LTS package with WinGet.");
        await install(cancellationToken);

        runtime = await FindCompatibleAsync(cancellationToken);
        return runtime ?? throw new InvalidOperationException(
            "WinGet completed, but a compatible Node.js runtime could not be found. " +
            $"Install Node.js {MinimumVersion}+ for {DescribeArchitecture(requiredArchitecture)} " +
            "and retry.");
    }

    private async Task<NodeRuntimeInfo?> FindCompatibleAsync(
        CancellationToken cancellationToken)
    {
        foreach (string candidate in findCandidates().Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            NodeRuntimeInfo? runtime = await probe(candidate, cancellationToken);
            if (runtime is null)
            {
                continue;
            }

            if (runtime.Architecture != requiredArchitecture)
            {
                log?.Invoke(
                    $"Ignoring Node.js {runtime.Version} at {candidate}: " +
                    $"architecture is {DescribeArchitecture(runtime.Architecture)}, " +
                    $"expected {DescribeArchitecture(requiredArchitecture)}.");
                continue;
            }

            if (runtime.Version < MinimumVersion)
            {
                log?.Invoke(
                    $"Ignoring Node.js {runtime.Version} at {candidate}: " +
                    $"minimum supported version is {MinimumVersion}.");
                continue;
            }

            log?.Invoke(
                $"Using Node.js {runtime.Version} from {runtime.ExecutablePath}.");
            return runtime;
        }

        return null;
    }

    private static string DescribeArchitecture(Architecture architecture) =>
        architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "ARM64",
            _ => architecture.ToString()
        };
}

internal static class SystemNodeRuntime
{
    private const string NodePathVariable = "OPENCLAW_NODE_PATH";

    public static NodeRuntimeResolver CreateResolver(Action<string>? log = null)
    {
        Architecture architecture = RuntimeInformation.ProcessArchitecture;
        if (architecture is not Architecture.X64 and not Architecture.Arm64)
        {
            throw new PlatformNotSupportedException(
                $"Unsupported process architecture: {architecture}.");
        }

        return new NodeRuntimeResolver(
            FindCandidates,
            ProbeAsync,
            cancellationToken => WinGetNodeInstaller.InstallAsync(
                log,
                cancellationToken),
            architecture,
            log);
    }

    internal static IReadOnlyList<string> FindCandidates()
    {
        string? configuredPath = Environment.GetEnvironmentVariable(
            NodePathVariable);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return [configuredPath.Trim().Trim('"')];
        }

        var candidates = new List<string>();
        AddPathCandidates(candidates, Environment.GetEnvironmentVariable("PATH"));
        AddPathCandidates(
            candidates,
            Environment.GetEnvironmentVariable(
                "PATH",
                EnvironmentVariableTarget.User));
        AddPathCandidates(
            candidates,
            Environment.GetEnvironmentVariable(
                "PATH",
                EnvironmentVariableTarget.Machine));

        foreach (string variable in new[]
                 {
                     "ProgramW6432",
                     "ProgramFiles",
                     "ProgramFiles(x86)"
                 })
        {
            string? root = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(root))
            {
                candidates.Add(Path.Combine(root, "nodejs", "node.exe"));
            }
        }

        return candidates;
    }

    internal static async Task<NodeRuntimeInfo?> ProbeAsync(
        string executablePath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add(
            "process.versions.node + '|' + process.arch");

        ProcessResult? result;
        try
        {
            result = await ProcessRunner.RunAsync(
                startInfo,
                TimeSpan.FromSeconds(15),
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            FileNotFoundException or
            TimeoutException or
            UnauthorizedAccessException)
        {
            return null;
        }

        if (result.ExitCode != 0)
        {
            return null;
        }

        return TryParseProbeOutput(executablePath, result.StandardOutput);
    }

    internal static NodeRuntimeInfo? TryParseProbeOutput(
        string executablePath,
        string output)
    {
        string[] fields = output.Trim().Split('|');
        if (fields.Length != 2 ||
            !Version.TryParse(fields[0], out Version? version))
        {
            return null;
        }

        Architecture? architecture = fields[1] switch
        {
            "x64" => Architecture.X64,
            "arm64" => Architecture.Arm64,
            _ => null
        };
        return architecture is null
            ? null
            : new NodeRuntimeInfo(
                Path.GetFullPath(executablePath),
                version,
                architecture.Value);
    }

    private static void AddPathCandidates(
        ICollection<string> candidates,
        string? pathValue)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return;
        }

        foreach (string entry in pathValue.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            string directory = Environment.ExpandEnvironmentVariables(
                entry.Trim('"'));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                candidates.Add(Path.Combine(directory, "node.exe"));
            }
        }
    }
}

internal static class WinGetNodeInstaller
{
    internal const string PackageId = "OpenJS.NodeJS.LTS";

    public static async Task InstallAsync(
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        string wingetPath = FindWinGet() ?? throw new FileNotFoundException(
            "Windows Package Manager (winget.exe) is required to install Node.js. " +
            "Install or update App Installer, then retry.");
        ProcessStartInfo startInfo = CreateStartInfo(wingetPath);

        log?.Invoke($"Running WinGet package installation for {PackageId}.");
        ProcessResult result = await ProcessRunner.RunAsync(
            startInfo,
            TimeSpan.FromMinutes(15),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"WinGet could not install {PackageId} " +
                $"(exit code {result.ExitCode}).");
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string wingetPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = wingetPath,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        foreach (string argument in new[]
                 {
                     "install",
                     "--id", PackageId,
                     "--exact",
                     "--source", "winget",
                     "--accept-package-agreements",
                     "--accept-source-agreements",
                     "--silent"
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static string? FindWinGet()
    {
        string windowsApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps",
            "winget.exe");
        if (File.Exists(windowsApps))
        {
            return windowsApps;
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return path.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(entry => Path.Combine(entry.Trim('"'), "winget.exe"))
            .FirstOrDefault(File.Exists);
    }
}

internal sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                $"Unable to start {startInfo.FileName}.");
        Task<string> standardOutput = startInfo.RedirectStandardOutput
            ? process.StandardOutput.ReadToEndAsync(cancellationToken)
            : Task.FromResult(string.Empty);
        Task<string> standardError = startInfo.RedirectStandardError
            ? process.StandardError.ReadToEndAsync(cancellationToken)
            : Task.FromResult(string.Empty);

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(linkedSource.Token);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            await KillAsync(process);
            throw new TimeoutException(
                $"{startInfo.FileName} did not finish within {timeout}.");
        }
        catch (OperationCanceledException)
        {
            await KillAsync(process);
            throw;
        }

        return new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static async Task KillAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }
}
