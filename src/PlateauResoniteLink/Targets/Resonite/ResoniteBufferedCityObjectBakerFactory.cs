using System;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteBufferedCityObjectBakerFactory
{
    CompositeCityObjectBaker? Create(
        bool enableMeshBake,
        ResoniteImportBudgetProfile resourceBudget);
}

internal sealed class ResoniteBufferedCityObjectBakerFactory(
    ResoniteTextureImageLoader textureImageLoader) : IResoniteBufferedCityObjectBakerFactory
{
    public CompositeCityObjectBaker? Create(
        bool enableMeshBake,
        ResoniteImportBudgetProfile resourceBudget)
    {
        ArgumentNullException.ThrowIfNull(textureImageLoader);

        _ = resourceBudget.Name switch
        {
            ResoniteImportMemoryProfile.Small or ResoniteImportMemoryProfile.Large => true,
            _ => throw new ArgumentOutOfRangeException(nameof(resourceBudget), resourceBudget.Name, "Unsupported memory profile."),
        };

        return enableMeshBake
            ? new CompositeCityObjectBaker(
                new NonDemCityObjectBaker(
                    bakePolicies: NonDemCityObjectBakePolicies.DefaultPolicies,
                    sourceFileBakeEmitter: new NonDemSourceFileBakeEmitterFactory(
                        textureImageLoader,
                        new NonDemAtlasBakeBudget(ResourceBudget: resourceBudget)).Create()))
            : null;
    }
}
