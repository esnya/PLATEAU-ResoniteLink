using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class NonDemCityObjectBakePolicyResolver(
    IReadOnlyList<NonDemCityObjectBakePolicy> bakePolicies)
{
    private readonly IReadOnlyList<NonDemCityObjectBakePolicy> bakePolicies = bakePolicies
        ?? throw new ArgumentNullException(nameof(bakePolicies));

    public NonDemCityObjectBakePolicy? Resolve(ResoniteConstructionCityObject cityObject)
    {
        foreach (NonDemCityObjectBakePolicy policy in bakePolicies)
        {
            if (policy.CanBuffer(cityObject)
                && NonDemCityObjectBakeMaterialClassifier.CanBufferCityObjectMaterials(cityObject, policy))
            {
                return policy;
            }
        }

        return null;
    }
}
