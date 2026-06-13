using System.Threading;
using System.Threading.Tasks;


namespace PlateauResoniteLink.Application.Importing;

internal sealed class LocalCityGmlDocumentReader : ICityGmlDocumentReader
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

    public async Task<ImportedSceneSourceSnapshot> ReadAsync(
        ResolvedLocalPlateauImportRequest request,
        CancellationToken cancellationToken = default)
    {
        return await ImportedSceneSourceDiscoveryPipeline.ReadDocumentSetCoreAsync(
            request,
            datasetContentSourceFactory,
            appearanceStoreFactory,
            lodSelector, cancellationToken);
    }
}
