using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class LocalCityGmlDocumentReader : ICityGmlDocumentReader
{
    private readonly Func<string, CancellationToken, Task<IPlateauDatasetContentSource>> createDatasetContentSource;
    private readonly Func<string, IPlateauDatasetContentSource, ICityGmlAppearanceStore> createAppearanceStore;
    private readonly ICityGmlLodSelector lodSelector;

    internal LocalCityGmlDocumentReader(
        Func<string, CancellationToken, Task<IPlateauDatasetContentSource>> createDatasetContentSource,
        Func<string, IPlateauDatasetContentSource, ICityGmlAppearanceStore> createAppearanceStore,
        ICityGmlLodSelector lodSelector)
    {
        this.createDatasetContentSource = createDatasetContentSource;
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
            createDatasetContentSource,
            createAppearanceStore,
            lodSelector,
            logger,
            cancellationToken);
    }
}
