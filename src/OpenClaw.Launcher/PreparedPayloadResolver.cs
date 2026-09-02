namespace OpenClaw.Launcher;

internal static class PreparedPayloadResolver
{
    public static async Task<string> ResolveAsync(
        HostOptions options,
        CancellationToken cancellationToken)
    {
        PayloadMetadata metadata = await PayloadMetadata.LoadAsync(
            options.MetadataPath,
            cancellationToken);
        PayloadMetadata.ValidateForCurrentProcess(metadata, options.PayloadPath);

        string? verifiedHash = await PayloadStager.ReadVerificationMarkerAsync(
            options.InstallDirectory,
            cancellationToken);
        if (!string.Equals(
            verifiedHash,
            metadata.Sha256,
            StringComparison.OrdinalIgnoreCase))
        {
            string state = verifiedHash is null
                ? "not prepared or is incomplete"
                : "out of date for the installed package";
            throw new InvalidOperationException(
                $"The packaged OpenClaw payload is {state}. " +
                "Run `clawctl prepare`, then retry.");
        }

        return options.InstallDirectory;
    }
}
