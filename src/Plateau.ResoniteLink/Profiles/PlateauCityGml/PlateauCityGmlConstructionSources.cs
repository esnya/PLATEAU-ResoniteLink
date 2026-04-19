using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public static class PlateauCityGmlConstructionSources
{
    internal static Func<IResoniteConstructionSourceFactory> FactoryProvider { get; set; } = CreateDefaultConstructionSourceFactory;

    public static Task<IResoniteConstructionSource> CreateAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return FactoryProvider().CreateAsync(
            request,
            progressReporter,
            cancellationToken);
    }

    public static IResoniteConstructionSource Create(
        PlateauImportRequest request,
        Action<string>? progressReporter = null)
    {
        return CreateAsync(request, progressReporter).GetAwaiter().GetResult();
    }

    internal static IResoniteConstructionSourceFactory CreateDefaultConstructionSourceFactory()
    {
        IPlateauDatasetContentSourceFactory datasetContentSourceFactory = new DefaultPlateauDatasetContentSourceFactory(
            new RemoteArchiveDistributionPolicy(),
            new ArchiveFileLayoutPolicy());
        ICityGmlAppearanceStoreFactory appearanceStoreFactory = new CityGmlAppearanceStoreFactory();
        ICityGmlLodSelector lodSelector = new CityGmlLodSelector();
        IDefaultMaterialResolver materialResolver = new DefaultMaterialResolver();
        ICityGmlDocumentReader documentReader = new LocalCityGmlDocumentReader(
            datasetContentSourceFactory,
            appearanceStoreFactory,
            lodSelector);
        ICityGmlGeometryProjector geometryProjector = new LocalCityGmlGeometryProjector(materialResolver);
        ICityGmlCommonMaterialEnumerator commonMaterialEnumerator = new LocalCityGmlCommonMaterialEnumerator(materialResolver);
        IResoniteConstructionComposer constructionComposer = new LocalCityGmlConstructionComposer(
            geometryProjector,
            commonMaterialEnumerator);
        return new LocalCityGmlConstructionSourceFactory(documentReader, constructionComposer);
    }
}
