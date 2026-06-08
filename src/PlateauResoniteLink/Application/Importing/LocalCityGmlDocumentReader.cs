using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class LocalCityGmlDocumentReader : ICityGmlDocumentReader
{
    private readonly Func<string, CancellationToken, Task<IPlateauDatasetContentSource>> createDatasetContentSource;
    private readonly Func<string, IPlateauDatasetContentSource, ICityGmlAppearanceStore> createAppearanceStore;

    internal LocalCityGmlDocumentReader(
        Func<string, CancellationToken, Task<IPlateauDatasetContentSource>> createDatasetContentSource,
        Func<string, IPlateauDatasetContentSource, ICityGmlAppearanceStore> createAppearanceStore)
    {
        this.createDatasetContentSource = createDatasetContentSource;
        this.createAppearanceStore = createAppearanceStore;
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
            logger,
            cancellationToken);
    }
}
