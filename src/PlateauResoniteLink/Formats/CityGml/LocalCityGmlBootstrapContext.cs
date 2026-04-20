namespace PlateauResoniteLink.Application.Importing;

public sealed class LocalCityGmlBootstrapContext
{
    internal LocalCityGmlBootstrapContext(
        IReadOnlyList<SourceFilePipeline> sourceFilePipelines,
        GeodeticPoint globalOriginPoint)
    {
        SourceFilePipelines = sourceFilePipelines;
        GlobalOriginPoint = globalOriginPoint;
    }

    internal IReadOnlyList<SourceFilePipeline> SourceFilePipelines { get; }

    internal GeodeticPoint GlobalOriginPoint { get; }
}
