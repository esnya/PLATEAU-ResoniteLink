using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed class LocalCityGmlDocumentReader : ICityGmlDocumentReader
{
    private readonly IPlateauDatasetContentSourceFactory datasetContentSourceFactory;
    private readonly ICityGmlAppearanceStoreFactory appearanceStoreFactory;
    private readonly ICityGmlLodSelector lodSelector;

    public LocalCityGmlDocumentReader()
        : this(
            new DefaultPlateauDatasetContentSourceFactory(
                new RemoteArchiveDistributionPolicy(),
                new ArchiveFileLayoutPolicy()),
            new CityGmlAppearanceStoreFactory(),
            new CityGmlLodSelector())
    {
    }

    internal LocalCityGmlDocumentReader(
        IPlateauDatasetContentSourceFactory datasetContentSourceFactory,
        ICityGmlAppearanceStoreFactory appearanceStoreFactory,
        ICityGmlLodSelector lodSelector)
    {
        this.datasetContentSourceFactory = datasetContentSourceFactory;
        this.appearanceStoreFactory = appearanceStoreFactory;
        this.lodSelector = lodSelector;
    }

    public async Task<LocalCityGmlDocumentSet> ReadAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return await LocalCityGmlBootstrapPipeline.ReadAsync(
            request,
            datasetContentSourceFactory,
            appearanceStoreFactory,
            lodSelector,
            progressReporter,
            cancellationToken);
    }
}
