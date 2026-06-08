using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class LocalCityGmlDocumentReader : ICityGmlDocumentReader
{
    private readonly IPlateauDatasetContentSourceFactory datasetContentSourceFactory;
    private readonly Func<string, IPlateauDatasetContentSource, ICityGmlAppearanceStore> createAppearanceStore;
    private readonly ICityGmlLodSelector lodSelector;

    internal LocalCityGmlDocumentReader(
        IPlateauDatasetContentSourceFactory datasetContentSourceFactory,
        Func<string, IPlateauDatasetContentSource, ICityGmlAppearanceStore> createAppearanceStore,
        ICityGmlLodSelector lodSelector)
    {
        this.datasetContentSourceFactory = datasetContentSourceFactory;
        this.createAppearanceStore = createAppearanceStore;
        this.lodSelector = lodSelector;
    }

    public async Task<ImportedSceneSourceSnapshot> ReadAsync(
        ResolvedLocalPlateauImportRequest request,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        return await ImportedSceneSourceDiscoveryPipeline.ReadDocumentSetCoreAsync(
            request,
            datasetContentSourceFactory,
            createAppearanceStore,
            lodSelector,
            logger,
            cancellationToken);
    }
}
