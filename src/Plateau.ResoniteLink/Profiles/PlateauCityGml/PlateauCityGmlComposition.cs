using Plateau.ResoniteLink.Application.Importing;

namespace Plateau.ResoniteLink.Profiles.PlateauCityGml;

public static class PlateauCityGmlComposition
{
    public static IResoniteConstructionSourceFactory CreateConstructionSourceFactory()
    {
        return new LocalCityGmlConstructionSourceFactory(
            new LocalCityGmlDocumentReader(),
            new LocalCityGmlConstructionComposer(
                new LocalCityGmlGeometryProjector(new DefaultMaterialResolver())));
    }
}
