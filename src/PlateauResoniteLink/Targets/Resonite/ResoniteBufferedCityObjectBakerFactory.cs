using System;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteBufferedCityObjectBakerFactory
{
    CompositeCityObjectBaker? Create(
        bool enableMeshBake,
        ResoniteImportBudgetProfile resourceBudget,
        ResoniteLocalOrigin requestLocalOrigin);
}

internal sealed class ResoniteBufferedCityObjectBakerFactory(
    INonDemSourceFileBakeEmitterFactory sourceFileBakeEmitterFactory) : IResoniteBufferedCityObjectBakerFactory
{
    private readonly INonDemSourceFileBakeEmitterFactory sourceFileBakeEmitterFactory = sourceFileBakeEmitterFactory
        ?? throw new ArgumentNullException(nameof(sourceFileBakeEmitterFactory));

    public CompositeCityObjectBaker? Create(
        bool enableMeshBake,
        ResoniteImportBudgetProfile resourceBudget,
        ResoniteLocalOrigin requestLocalOrigin)
    {
        _ = resourceBudget.Name switch
        {
            ResoniteImportMemoryProfile.Small or ResoniteImportMemoryProfile.Large => true,
            _ => throw new ArgumentOutOfRangeException(nameof(resourceBudget), resourceBudget.Name, "Unsupported memory profile."),
        };

        return enableMeshBake
            ? new CompositeCityObjectBaker(
                new NonDemCityObjectBaker(
                    bakePolicyResolver: new NonDemCityObjectBakePolicyResolver(NonDemCityObjectBakePolicies.DefaultPolicies),
                    sourceFileBakeEmitter: sourceFileBakeEmitterFactory.Create(
                        new NonDemAtlasBakeBudget(ResourceBudget: resourceBudget),
                        requestLocalOrigin)))
            : null;
    }
}
