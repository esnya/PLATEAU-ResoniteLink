using System;

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
            ResoniteImportMemoryProfile.Small => 32_768,
            ResoniteImportMemoryProfile.Large => 65_535,
            _ => throw new ArgumentOutOfRangeException(nameof(resourceBudget), resourceBudget.Name, "Unsupported memory profile."),
        };
        int maxCityObjectsPerBatch = resourceBudget.Name switch
        {
            ResoniteImportMemoryProfile.Small => 512,
            ResoniteImportMemoryProfile.Large => 4096,
            _ => throw new ArgumentOutOfRangeException(nameof(resourceBudget), resourceBudget.Name, "Unsupported memory profile."),
        };
        int maxBufferedCells = resourceBudget.Name switch
        {
            ResoniteImportMemoryProfile.Small => 256,
            ResoniteImportMemoryProfile.Large => 1024,
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
