namespace PlateauResoniteLink.Domain.Importing;

public sealed record SceneBootstrapInfo(
    string Dataset,
    string MeshCode,
    string LocalSourcePath,
    IReadOnlyList<string> PackageNames,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string> RequestedMeshCodes,
    LicenseAttributionMetadata DatasetLicense,
    IReadOnlyList<LicenseAttributionMetadata> AdditionalDatasetLicenses)
{
    private const string GsiLicenseName = "GSI Maps Terms";
    private const string GsiLicenseUrl = "https://maps.gsi.go.jp/help/termsofuse.html";

    public static SceneBootstrapInfo CreateFromMetadata(
        ResoniteConstructionMetadata metadata,
        string? localSourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        return new SceneBootstrapInfo(
            metadata.Request.Dataset,
            metadata.Request.MeshCode,
            localSourcePath
                ?? metadata.Request.LocalSourcePath
                ?? string.Empty,
            metadata.SourceDataset.PackageNames,
            metadata.SourceDataset.SourceFiles,
            metadata.SourceDataset.RequestedMeshCodes ?? [],
            metadata.Attribution.DatasetLicense,
            CreateAdditionalDatasetLicenses(metadata));
    }

    private static LicenseAttributionMetadata[] CreateAdditionalDatasetLicenses(
        ResoniteConstructionMetadata metadata)
    {
        return metadata.SourceDataset.TerrainTextureOverlays
            .Select(static overlay => overlay.LicenseMode)
            .Where(static licenseMode => licenseMode == TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback)
            .Select(static _ => new LicenseAttributionMetadata(
                RequireCredit: true,
                CreditText: "DEM terrain imagery may use fallback to GSI seamless photo tiles where PLATEAU-Ortho coverage is unavailable.",
                LicenseName: GsiLicenseName,
                LicenseUrl: GsiLicenseUrl))
            .Distinct()
            .ToArray();
    }
}
