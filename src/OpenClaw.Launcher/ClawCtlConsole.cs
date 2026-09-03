namespace OpenClaw.Launcher;

public static class ClawCtlConsole
{
    public static void WriteHelp(TextWriter output)
    {
        output.WriteLine("clawctl - OpenClaw package preparation");
        output.WriteLine();
        WriteUsage(output);
        output.WriteLine();
        output.WriteLine("Commands:");
        output.WriteLine("  setup   Set up the packaged OpenClaw payload.");
        output.WriteLine();
        WriteNodePrerequisite(output);
        output.WriteLine();
        output.WriteLine("Run `openclaw <arguments>` to invoke the OpenClaw CLI.");
    }

    public static void WriteUsage(TextWriter output) =>
        output.WriteLine("Usage: clawctl setup");

    public static void WriteNodePrerequisite(TextWriter output)
    {
        output.WriteLine(
            $"Prerequisite: install Node.js {NodeRuntimeResolver.SupportedVersions}.");
        output.WriteLine($"  {NodeRuntimeResolver.InstallCommand}");
    }

    internal static void WriteNodeRuntimeSummary(
        TextWriter output,
        NodeRuntime runtime) =>
        output.WriteLine(
            $"Using Node.js {runtime.Version} from {runtime.ExecutablePath}");

    public static void WritePreparationSummary(
        TextWriter output,
        StagedPayload payload)
    {
        output.WriteLine();
        output.WriteLine("OpenClaw package files are ready.");
        output.WriteLine(
            payload.Reused
                ? "The existing prepared payload is current and was reused."
                : "The packaged payload was verified and prepared.");
        output.WriteLine($"Prepared files: {payload.DirectoryPath}");
    }
}
