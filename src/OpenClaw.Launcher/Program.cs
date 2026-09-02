using System.Reflection;

namespace OpenClaw.Launcher;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        HostEntrypoint entrypoint = HostEntrypointResolver.Resolve();
        string commandName = entrypoint == HostEntrypoint.Control
            ? HostEntrypointResolver.ControlCommandName
            : HostEntrypointResolver.AgentCommandName;
        HostDiagnosticLog? diagnostics = null;
        bool diagnosticWarningWritten = false;
        bool consoleWarningWritten = false;

        void WriteConsoleError(string message)
        {
            try
            {
                Console.Error.WriteLine(message);
            }
            catch (Exception exception) when (
                exception is IOException or ObjectDisposedException)
            {
                if (!consoleWarningWritten)
                {
                    consoleWarningWritten = true;
                    WriteDiagnostic(
                        $"Console error output failed: {exception.GetType().Name}.");
                }
            }
        }

        try
        {
            diagnostics = HostDiagnosticLog.Create();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            diagnosticWarningWritten = true;
            WriteConsoleError(
                $"{commandName}: Unable to create diagnostics: {exception.Message}");
        }

        void WriteDiagnostic(string message)
        {
            if (diagnostics is null)
            {
                return;
            }

            try
            {
                diagnostics.Write(message);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                ObjectDisposedException)
            {
                if (!diagnosticWarningWritten)
                {
                    diagnosticWarningWritten = true;
                    WriteConsoleError(
                        $"{commandName}: Unable to write diagnostics: {exception.Message}");
                }
            }
        }

        static string GetDiagnosticFailure(Exception exception) =>
            exception switch
            {
                InvalidDataException or
                TimeoutException or
                PlatformNotSupportedException or
                FileNotFoundException =>
                    $"{exception.GetType().Name}: {exception.Message}",
                _ => exception.GetType().Name
            };

        try
        {
            WriteDiagnostic($"Host started through the {commandName} entrypoint.");
            HostOptions options = HostOptions.Parse(args);
            return entrypoint == HostEntrypoint.Control
                ? await RunControlAsync(options, args, WriteDiagnostic, WriteConsoleError)
                : await RunAgentAsync(options, WriteDiagnostic);
        }
        catch (Exception exception)
        {
            WriteDiagnostic($"Unhandled failure: {GetDiagnosticFailure(exception)}");
            WriteConsoleError($"{commandName}: {exception.Message}");
            if (diagnostics is not null)
            {
                WriteConsoleError(
                    $"{commandName}: See diagnostics: {diagnostics.Path}");
            }
            return 1;
        }
        finally
        {
            WriteDiagnostic("Host exiting.");
            diagnostics?.Dispose();
        }
    }

    internal static async Task<int> RunAgentAsync(
        HostOptions options,
        Action<string> log,
        Func<CancellationToken, Task<NodeRuntime>>? resolveNode = null)
    {
        await PreparedPayloadResolver.ResolveAsync(
            options,
            CancellationToken.None);
        NodeRuntime nodeRuntime = await (
            resolveNode ?? NodeRuntimeResolver.ResolveAsync)(
                CancellationToken.None);
        log(
            $"Using Node.js {nodeRuntime.Version} from " +
            $"{nodeRuntime.ExecutablePath}.");
        using FileStream runtimeLease = PayloadRuntimeLock.AcquireForLaunch(
            options.InstallDirectory);
        string payloadDirectory = await PreparedPayloadResolver.ResolveAsync(
            options,
            CancellationToken.None);
        return await GatewayLauncher.RunAsync(
            nodeRuntime.ExecutablePath,
            payloadDirectory,
            options.OpenClawArguments,
            CancellationToken.None,
            log);
    }

    private static async Task<int> RunControlAsync(
        HostOptions options,
        IReadOnlyList<string> args,
        Action<string> log,
        Action<string> writeError)
    {
        ClawCtlCommandParseResult parsed = ClawCtlCommandParser.Parse(args);
        if (parsed.Error is not null)
        {
            writeError($"clawctl: {parsed.Error}");
            ClawCtlConsole.WriteUsage(Console.Error);
            return 2;
        }

        switch (parsed.Command)
        {
            case ClawCtlCommand.Help:
                ClawCtlConsole.WriteHelp(Console.Out);
                return 0;
            case ClawCtlCommand.Version:
                Console.Out.WriteLine(
                    Assembly.GetExecutingAssembly().GetName().Version?.ToString() ??
                    "unknown");
                return 0;
            case ClawCtlCommand.Verify:
            {
                NodeRuntime nodeRuntime = await NodeRuntimeResolver.ResolveAsync(
                    CancellationToken.None);
                ClawCtlConsole.WriteNodeRuntimeSummary(Console.Out, nodeRuntime);
                var verifier = new PayloadStager(options.InstallDirectory, log);
                PayloadVerification verification = await verifier.VerifyAsync(
                    options.PayloadPath,
                    options.MetadataPath,
                    CancellationToken.None);
                ClawCtlConsole.WriteVerificationSummary(Console.Out, verification);
                return verification.IsValid ? 0 : 1;
            }
            case ClawCtlCommand.Prepare:
            case ClawCtlCommand.Repair:
            {
                NodeRuntime nodeRuntime = await NodeRuntimeResolver.ResolveAsync(
                    CancellationToken.None);
                ClawCtlConsole.WriteNodeRuntimeSummary(Console.Out, nodeRuntime);
                bool repair = parsed.Command == ClawCtlCommand.Repair;
                void ReportProgress(string message)
                {
                    log(message);
                    writeError($"clawctl: {message}");
                }

                var stager = new PayloadStager(
                    options.InstallDirectory,
                    ReportProgress,
                    verifyInstalledPayload: repair);
                StagedPayload payload = await stager.StageAsync(
                    options.PayloadPath,
                    options.MetadataPath,
                    CancellationToken.None);
                ClawCtlConsole.WritePreparationSummary(
                    Console.Out,
                    payload,
                    repair);
                return 0;
            }
            default:
                throw new InvalidOperationException("Unknown clawctl command.");
        }
    }
}
