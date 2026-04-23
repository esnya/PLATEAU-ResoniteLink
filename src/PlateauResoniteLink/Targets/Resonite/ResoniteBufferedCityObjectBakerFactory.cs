using System;
using System.Linq;

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

        int maxBufferedCityObjectsPerSourceUnit = resourceBudget.Name switch
        {
            ResoniteImportMemoryProfile.Small => 512,
            ResoniteImportMemoryProfile.Large => 4096,
            _ => throw new ArgumentOutOfRangeException(nameof(resourceBudget), resourceBudget.Name, "Unsupported memory profile."),
        };
        int maxBufferedSourceUnits = resourceBudget.Name switch
        {
            ResoniteImportMemoryProfile.Small => 256,
            ResoniteImportMemoryProfile.Large => 1024,
            _ => throw new ArgumentOutOfRangeException(nameof(resourceBudget), resourceBudget.Name, "Unsupported memory profile."),
        };

        return enableMeshBake
            ? new CompositeCityObjectBaker(
                new ScopedBufferedCityObjectBaker(
                    "NonDemBake",
                    () => new NonDemCityObjectBaker(
                        textureImageLoader,
                        resourceBudget: resourceBudget,
                        maxBufferedCityObjectsPerSourceUnit: maxBufferedCityObjectsPerSourceUnit),
                    static cityObject => NonDemCityObjectBakePolicies.DefaultPolicies.Any(policy => policy.CanBuffer(cityObject)),
                    maxBufferedScopes: maxBufferedSourceUnits))
            : null;
    }
}
