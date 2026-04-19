using Plateau.ResoniteLink.Application.Importing;

namespace Plateau.ResoniteLink.Profiles.PlateauCityGml;

internal static class PlateauCityGmlImportComposition
{
    public static ICityGmlDocumentReader CreateDocumentReader(
        IPlateauDatasetContentSourceFactory datasetContentSourceFactory,
        ICityGmlAppearanceStoreFactory appearanceStoreFactory,
        ICityGmlLodSelector lodSelector)
    {
        ArgumentNullException.ThrowIfNull(datasetContentSourceFactory);
        ArgumentNullException.ThrowIfNull(appearanceStoreFactory);
        ArgumentNullException.ThrowIfNull(lodSelector);
        return new LocalCityGmlDocumentReader(datasetContentSourceFactory, appearanceStoreFactory, lodSelector);
    }

    public static IDefaultMaterialResolver CreateMaterialResolver()
    {
        return new DefaultMaterialResolver();
    }

    public static ICityGmlGeometryProjector CreateGeometryProjector(IDefaultMaterialResolver materialResolver)
    {
        ArgumentNullException.ThrowIfNull(materialResolver);
        return new LocalCityGmlGeometryProjector(materialResolver);
    }

    public static IResoniteConstructionComposer CreateConstructionComposer(ICityGmlGeometryProjector geometryProjector)
    {
        ArgumentNullException.ThrowIfNull(geometryProjector);
        return new LocalCityGmlConstructionComposer(geometryProjector);
    }

    public static IResoniteConstructionSourceFactory CreateConstructionSourceFactory(
        ICityGmlDocumentReader documentReader,
        IResoniteConstructionComposer constructionComposer)
    {
        ArgumentNullException.ThrowIfNull(documentReader);
        ArgumentNullException.ThrowIfNull(constructionComposer);
        return new LocalCityGmlConstructionSourceFactory(documentReader, constructionComposer);
    }
}
