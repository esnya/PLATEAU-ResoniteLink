using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

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

        int maxVerticesPerBatch = resourceBudget.Name switch
        {
            PlateauImportMemoryProfile.Small => 32_768,
            PlateauImportMemoryProfile.Large => 65_535,
            _ => throw new ArgumentOutOfRangeException(nameof(resourceBudget), resourceBudget.Name, "Unsupported memory profile."),
        };
        int maxCityObjectsPerBatch = resourceBudget.Name switch
        {
            PlateauImportMemoryProfile.Small => 512,
            PlateauImportMemoryProfile.Large => 4096,
            _ => throw new ArgumentOutOfRangeException(nameof(resourceBudget), resourceBudget.Name, "Unsupported memory profile."),
        };
        int maxBufferedCells = resourceBudget.Name switch
        {
            PlateauImportMemoryProfile.Small => 256,
            PlateauImportMemoryProfile.Large => 1024,
            _ => throw new ArgumentOutOfRangeException(nameof(resourceBudget), resourceBudget.Name, "Unsupported memory profile."),
        };

        return enableMeshBake
            ? new CompositeCityObjectBaker(
                new Lod2AtlasCityObjectBaker(textureImageLoader, resourceBudget: resourceBudget),
                new FixedCellCityObjectMeshBaker(
                    FixedCellCityObjectMeshBaker.DefaultCellSizeMeters,
                    maxCityObjectsPerBatch,
                    maxVerticesPerBatch,
                    maxBufferedCells))
            : null;
    }
}
