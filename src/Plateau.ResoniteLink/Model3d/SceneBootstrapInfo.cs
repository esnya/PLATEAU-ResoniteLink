namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record SceneBootstrapInfo(
    string Dataset,
    string MeshCode,
    string LocalSourcePath,
    IReadOnlyList<string> PackageNames,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string> RequestedMeshCodes,
    LicenseAttributionMetadata DatasetLicense,
    bool RequiresGsiFallbackLicense)
{
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
            metadata.SourceDataset.TerrainTextureOverlays.Any(static overlay => overlay.LicenseMode == TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback));
    }
}
