namespace Plateau.ResoniteLink.Application.Importing;

public static class PlateauImportApplicationComposition
{
    public static IResoniteConstructionSourceFactory CreateConstructionSourceFactory()
    {
        return new LocalCityGmlConstructionSourceFactory(
            new LocalCityGmlDocumentReader(),
            new LocalCityGmlConstructionComposer(
                new LocalCityGmlGeometryProjector(new DefaultMaterialResolver())));
    }
}
