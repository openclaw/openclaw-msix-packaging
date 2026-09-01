namespace OpenClaw.WindowsLauncher;

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
            throw new InvalidOperationException(
                "The packaged OpenClaw payload is not prepared. " +
                "Run `clawctl prepare`, then retry.");
        }

        return options.InstallDirectory;
    }
}
