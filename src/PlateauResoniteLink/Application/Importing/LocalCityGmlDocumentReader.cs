using System;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class LocalCityGmlDocumentReader : ICityGmlDocumentReader
{
    private readonly Func<string, CancellationToken, Task<IPlateauDatasetContentSource>> createDatasetContentSource;
    private readonly Func<string, IPlateauDatasetContentSource, CityGmlAppearanceStore> createAppearanceStore;

    internal LocalCityGmlDocumentReader(
        Func<string, CancellationToken, Task<IPlateauDatasetContentSource>> createDatasetContentSource,
        Func<string, IPlateauDatasetContentSource, CityGmlAppearanceStore> createAppearanceStore)
    {
        this.createDatasetContentSource = createDatasetContentSource;
        this.createAppearanceStore = createAppearanceStore;
    }

    public async Task<ImportedSceneSourceSnapshot> ReadAsync(
        ResolvedLocalPlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return await ImportedSceneSourceDiscoveryPipeline.ReadDocumentSetCoreAsync(
            request,
            createDatasetContentSource,
            createAppearanceStore,
            progressReporter,
            cancellationToken);
    }
}
