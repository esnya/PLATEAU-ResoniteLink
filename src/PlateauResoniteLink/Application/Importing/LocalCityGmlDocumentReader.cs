using System;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

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

    public async Task<LocalCityGmlDocumentReadResult> ReadAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return await LocalCityGmlBootstrapPipeline.ReadDocumentSetCoreAsync(
            request,
            datasetContentSourceFactory,
            appearanceStoreFactory,
            lodSelector,
            progressReporter,
            cancellationToken);
    }
}
