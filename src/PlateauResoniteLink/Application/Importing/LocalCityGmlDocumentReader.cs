using System;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class LocalCityGmlDocumentReader
{
    private readonly Func<string, CancellationToken, Task<IPlateauDatasetContentSource>> createDatasetContentSource;
    private readonly Func<string, IPlateauDatasetContentSource, ICityGmlAppearanceStore> createAppearanceStore;
    private readonly SelectCityGmlLod selectLod;

    internal LocalCityGmlDocumentReader(
        Func<string, CancellationToken, Task<IPlateauDatasetContentSource>> createDatasetContentSource,
        Func<string, IPlateauDatasetContentSource, ICityGmlAppearanceStore> createAppearanceStore,
        SelectCityGmlLod selectLod)
    {
        this.createDatasetContentSource = createDatasetContentSource;
        this.createAppearanceStore = createAppearanceStore;
        this.selectLod = selectLod;
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
            selectLod,
            progressReporter,
            cancellationToken);
    }
}
