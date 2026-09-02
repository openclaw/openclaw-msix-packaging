namespace OpenClaw.Launcher;

public enum ClawCtlCommand
{
    Help,
    Version,
    Prepare
}

public sealed record ClawCtlCommandParseResult(
    ClawCtlCommand Command,
    string? Error = null);

public static class ClawCtlCommandParser
{
    public static ClawCtlCommandParseResult Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || IsSingle(args, "--help") || IsSingle(args, "-h"))
        {
            return new ClawCtlCommandParseResult(ClawCtlCommand.Help);
        }

        if (IsSingle(args, "--version"))
        {
            return new ClawCtlCommandParseResult(ClawCtlCommand.Version);
        }

        if (IsSingle(args, "prepare"))
        {
            return new ClawCtlCommandParseResult(ClawCtlCommand.Prepare);
        }

        return new ClawCtlCommandParseResult(
            ClawCtlCommand.Help,
            $"Unknown command or option: {string.Join(' ', args)}");
    }

    private static bool IsSingle(IReadOnlyList<string> args, string value) =>
        args.Count == 1 &&
        string.Equals(args[0], value, StringComparison.Ordinal);
}
