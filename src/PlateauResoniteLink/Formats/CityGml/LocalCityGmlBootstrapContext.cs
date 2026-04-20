namespace PlateauResoniteLink.Application.Importing;

public sealed class LocalCityGmlBootstrapContext
{
    internal LocalCityGmlBootstrapContext(
        IReadOnlyList<SourceFilePipeline> sourceFilePipelines,
        GeodeticPoint globalOriginPoint,
        DemTerrainGeoReferencedRasterCatalog? demRasterCatalog = null)
    {
        SourceFilePipelines = sourceFilePipelines;
        GlobalOriginPoint = globalOriginPoint;
        DemRasterCatalog = demRasterCatalog;
    }

    internal IReadOnlyList<SourceFilePipeline> SourceFilePipelines { get; }

    internal GeodeticPoint GlobalOriginPoint { get; }

    internal DemTerrainGeoReferencedRasterCatalog? DemRasterCatalog { get; }
}
