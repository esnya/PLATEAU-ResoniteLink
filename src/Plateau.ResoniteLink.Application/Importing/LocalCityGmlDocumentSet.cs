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
        LocalCityGmlResonitePlanBuilder.SourceFilePipeline[] sourceFilePipelines,
        IReadOnlyList<LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor> cachedDemSourceFiles,
        LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem referenceSystem,
        LocalCityGmlResonitePlanBuilder.GeodeticPoint globalOriginPoint,
        LocalCityGmlResonitePlanBuilder.TerrainHeightSampler? terrainHeightSampler)
    {
        DatasetSource = datasetSource;
        RelativeSourceFiles = relativeSourceFiles;
        PackageNames = packageNames;
        TerrainTextureOverlays = terrainTextureOverlays;
        RequestedMeshCodes = requestedMeshCodes;
        SourceFilePipelines = sourceFilePipelines;
        CachedDemSourceFiles = cachedDemSourceFiles;
        ReferenceSystem = referenceSystem;
        GlobalOriginPoint = globalOriginPoint;
        TerrainHeightSampler = terrainHeightSampler;
    }

    public IPlateauDatasetContentSource DatasetSource { get; }

    public IReadOnlyList<string> RelativeSourceFiles { get; }

    public IReadOnlyList<string> PackageNames { get; }

    public IReadOnlyList<TerrainTextureOverlay> TerrainTextureOverlays { get; }

    public IReadOnlyList<string> RequestedMeshCodes { get; }

    internal LocalCityGmlResonitePlanBuilder.SourceFilePipeline[] SourceFilePipelines { get; }

    internal IReadOnlyList<LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor> CachedDemSourceFiles { get; }

    internal LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem ReferenceSystem { get; }

    internal LocalCityGmlResonitePlanBuilder.GeodeticPoint GlobalOriginPoint { get; }

    internal LocalCityGmlResonitePlanBuilder.TerrainHeightSampler? TerrainHeightSampler { get; }
}
