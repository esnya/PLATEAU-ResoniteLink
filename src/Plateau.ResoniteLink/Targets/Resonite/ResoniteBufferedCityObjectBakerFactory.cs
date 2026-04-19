namespace Plateau.ResoniteLink.Targets.Resonite;

internal interface IResoniteBufferedCityObjectBakerFactory
{
    CompositeCityObjectBaker? Create(
        bool enableMeshBake,
        ResoniteTextureImageLoader textureImageLoader,
        ResoniteImportBudgetProfile resourceBudget);
}

internal sealed class ResoniteBufferedCityObjectBakerFactory : IResoniteBufferedCityObjectBakerFactory
{
    public CompositeCityObjectBaker? Create(
        bool enableMeshBake,
        ResoniteTextureImageLoader textureImageLoader,
        ResoniteImportBudgetProfile resourceBudget)
    {
        ArgumentNullException.ThrowIfNull(textureImageLoader);

        return enableMeshBake
            ? new CompositeCityObjectBaker(
                new Lod2AtlasCityObjectBaker(textureImageLoader, resourceBudget: resourceBudget),
                new FixedCellCityObjectMeshBaker())
            : null;
    }
}
