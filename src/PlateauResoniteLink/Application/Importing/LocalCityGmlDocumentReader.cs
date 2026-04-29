using System;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class LocalCityGmlDocumentReader : ICityGmlDocumentReader
{
    private readonly IPlateauDatasetContentSourceFactory datasetContentSourceFactory;
    private readonly ICityGmlAppearanceStoreFactory appearanceStoreFactory;
    private readonly ICityGmlSourceRepresentationSelector sourceRepresentationSelector;

    internal LocalCityGmlDocumentReader(
        IPlateauDatasetContentSourceFactory datasetContentSourceFactory,
        ICityGmlAppearanceStoreFactory appearanceStoreFactory,
        ICityGmlSourceRepresentationSelector sourceRepresentationSelector)
    {
        this.datasetContentSourceFactory = datasetContentSourceFactory;
        this.appearanceStoreFactory = appearanceStoreFactory;
        this.sourceRepresentationSelector = sourceRepresentationSelector;
    }

    public async Task<ImportedSceneSourceSnapshot> ReadAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return await ImportedSceneSourceDiscoveryPipeline.ReadDocumentSetCoreAsync(
            request,
            datasetContentSourceFactory,
            appearanceStoreFactory,
            sourceRepresentationSelector,
            progressReporter,
            cancellationToken);
    }
}
