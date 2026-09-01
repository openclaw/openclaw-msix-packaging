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
        output.WriteLine("  prepare   Prepare the packaged OpenClaw payload.");
        output.WriteLine("  verify    Verify the prepared payload without changing it.");
        output.WriteLine("  repair    Verify and recreate the prepared payload if needed.");
        output.WriteLine();
        WriteNodePrerequisite(output);
        output.WriteLine();
        output.WriteLine("Run `openclaw <arguments>` to invoke the OpenClaw CLI.");
    }

    public static void WriteUsage(TextWriter output) =>
        output.WriteLine("Usage: clawctl <prepare|verify|repair>");

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
        StagedPayload payload,
        bool repair)
    {
        output.WriteLine();
        output.WriteLine("OpenClaw package files are ready.");
        output.WriteLine(
            payload.Reused
                ? repair
                    ? "The existing prepared payload passed full verification."
                    : "The existing prepared payload is current and was reused."
                : repair
                    ? "The prepared payload was recreated from packaged content."
                    : "The packaged payload was verified and prepared.");
        output.WriteLine($"Prepared files: {payload.DirectoryPath}");
    }

    public static void WriteVerificationSummary(
        TextWriter output,
        PayloadVerification verification)
    {
        output.WriteLine(
            verification.IsValid
                ? "The prepared OpenClaw payload is valid."
                : $"The prepared OpenClaw payload is invalid: {verification.Detail}");
    }
}
