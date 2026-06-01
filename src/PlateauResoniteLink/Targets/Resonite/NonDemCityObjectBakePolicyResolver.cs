using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface INonDemCityObjectBakePolicyResolver
{
    NonDemCityObjectBakePolicy? Resolve(NonDemSourceScopedTriangleCityObject cityObject);
}

internal sealed class NonDemCityObjectBakePolicyResolver(
    IReadOnlyList<NonDemCityObjectBakePolicy> bakePolicies) : INonDemCityObjectBakePolicyResolver
{
    private readonly IReadOnlyList<NonDemCityObjectBakePolicy> bakePolicies = bakePolicies
        ?? throw new ArgumentNullException(nameof(bakePolicies));

    public NonDemCityObjectBakePolicy? Resolve(NonDemSourceScopedTriangleCityObject cityObject)
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
