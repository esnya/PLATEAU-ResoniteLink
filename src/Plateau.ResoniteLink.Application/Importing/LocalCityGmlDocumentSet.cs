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

    internal LocalCityGmlDocumentSet(
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<string> relativeSourceFiles,
        IReadOnlyList<string> packageNames,
        IReadOnlyList<TerrainTextureOverlay> terrainTextureOverlays,
        IReadOnlyList<string> requestedMeshCodes,
        LocalCityGmlResonitePlanBuilder.SourceFilePipeline[] sourceFilePipelines,
        IReadOnlyList<LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor> cachedDemSourceFiles,
        LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem referenceSystem,
        LocalCityGmlResonitePlanBuilder.GeodeticPoint globalOriginPoint,
        LocalCityGmlResonitePlanBuilder.TerrainHeightSampler? terrainHeightSampler)
        : this(
            datasetSource,
            relativeSourceFiles,
            packageNames,
            terrainTextureOverlays,
            requestedMeshCodes,
            sourceFilePipelines.Select(static pipeline => new SourceFilePipeline(pipeline)).ToArray(),
            cachedDemSourceFiles.Select(CachedSourceFileDescriptor.FromLegacy).ToArray(),
            CoordinateReferenceSystem.FromLegacy(referenceSystem),
            GeodeticPoint.FromLegacy(globalOriginPoint),
            global::Plateau.ResoniteLink.Application.Importing.TerrainHeightSampler.FromLegacy(terrainHeightSampler))
    {
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
