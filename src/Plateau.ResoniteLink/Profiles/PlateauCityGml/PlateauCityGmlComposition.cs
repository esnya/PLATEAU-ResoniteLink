using Plateau.ResoniteLink.Application.Importing;

namespace Plateau.ResoniteLink.Profiles.PlateauCityGml;

public static class PlateauCityGmlComposition
{
    public static IResoniteConstructionSourceFactory CreateConstructionSourceFactory()
    {
        return PlateauCityGmlImportComposition.CreateConstructionSourceFactory();
    }
}
