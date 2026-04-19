using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed class LocalCityGmlDocumentReader(IPlateauDatasetContentSourceFactory datasetContentSourceFactory) : ICityGmlDocumentReader
{
    public LocalCityGmlDocumentReader()
        : this(new DefaultPlateauDatasetContentSourceFactory(
            new RemoteArchiveDistributionPolicy(),
            new ArchiveFileLayoutPolicy()))
    {
    }

    public async Task<LocalCityGmlDocumentSet> ReadAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return await LocalCityGmlBootstrapPipeline.ReadAsync(
            request,
            datasetContentSourceFactory,
            progressReporter,
            cancellationToken);
    }
}
