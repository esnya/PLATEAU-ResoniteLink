using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed class LocalCityGmlDocumentReader : ICityGmlDocumentReader
{
    private readonly IPlateauDatasetContentSourceFactory datasetContentSourceFactory;
    private readonly ICityGmlAppearanceStoreFactory appearanceStoreFactory;
    private readonly ICityGmlLodSelector lodSelector;

    internal LocalCityGmlDocumentReader(
        IPlateauDatasetContentSourceFactory datasetContentSourceFactory,
        ICityGmlAppearanceStoreFactory appearanceStoreFactory,
        ICityGmlLodSelector lodSelector)
    {
        this.datasetContentSourceFactory = datasetContentSourceFactory;
        this.appearanceStoreFactory = appearanceStoreFactory;
        this.lodSelector = lodSelector;
    }

    public async Task<LocalCityGmlDocumentReadResult> ReadAsync(
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
