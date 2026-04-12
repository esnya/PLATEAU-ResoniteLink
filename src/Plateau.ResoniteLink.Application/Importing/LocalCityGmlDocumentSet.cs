using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed class LocalCityGmlDocumentSet
{
    internal LocalCityGmlDocumentSet(
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<string> relativeSourceFiles,
        IReadOnlyList<string> packageNames,
        IReadOnlyList<TerrainTextureOverlay> terrainTextureOverlays,
        IReadOnlyList<string> requestedMeshCodes,
        IReadOnlyList<SourceFilePipeline> sourceFilePipelines,
        IReadOnlyList<CachedSourceFileDescriptor> cachedDemSourceFiles,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        TerrainHeightSampler? terrainHeightSampler)
    {
        DatasetSource = datasetSource;
        RelativeSourceFiles = relativeSourceFiles;
        PackageNames = packageNames;
        TerrainTextureOverlays = terrainTextureOverlays;
        RequestedMeshCodes = requestedMeshCodes;
        BootstrapSourceFilePipelines = sourceFilePipelines;
        BootstrapCachedDemSourceFiles = cachedDemSourceFiles;
        BootstrapReferenceSystem = referenceSystem;
        BootstrapGlobalOriginPoint = globalOriginPoint;
        BootstrapTerrainHeightSampler = terrainHeightSampler;
    }

    public IPlateauDatasetContentSource DatasetSource { get; }

    public IReadOnlyList<string> RelativeSourceFiles { get; }

    public IReadOnlyList<string> PackageNames { get; }

    public IReadOnlyList<TerrainTextureOverlay> TerrainTextureOverlays { get; }

    public IReadOnlyList<string> RequestedMeshCodes { get; }

    internal IReadOnlyList<SourceFilePipeline> BootstrapSourceFilePipelines { get; }

    internal IReadOnlyList<CachedSourceFileDescriptor> BootstrapCachedDemSourceFiles { get; }

    internal CoordinateReferenceSystem BootstrapReferenceSystem { get; }

    internal GeodeticPoint BootstrapGlobalOriginPoint { get; }

    internal TerrainHeightSampler? BootstrapTerrainHeightSampler { get; }
}
