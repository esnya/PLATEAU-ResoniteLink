using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Targets.Resonite;

internal enum NonDemMaterialBakeCategory
{
    AtlasCandidate,
    PreservedCommonMaterial,
    PreservedVertexColor,
    PreservedTextureless,
    PreservedOther,
}

internal sealed record NonDemCityObjectBakePolicy(
    string Name,
    Func<NonDemSourceScopedTriangleCityObject, bool> CanBufferCityObject,
    bool RequireAtlasCandidateMaterial,
    bool PreserveVertexColorMaterials,
    bool PreserveTexturelessMaterials,
    bool PreserveCommonMaterials)
{
    public bool CanBuffer(NonDemSourceScopedTriangleCityObject cityObject)
    {
        return CanBufferCityObject(cityObject);
    }
}

internal static class NonDemCityObjectBakePolicies
{
    internal static readonly NonDemCityObjectBakePolicy Default = new(
        Name: "non-dem",
        CanBufferCityObject: static cityObject =>
            cityObject.Transform.Rotation is null
            && !string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase),
        RequireAtlasCandidateMaterial: false,
        PreserveVertexColorMaterials: true,
        PreserveTexturelessMaterials: true,
        PreserveCommonMaterials: true);

    internal static readonly IReadOnlyList<NonDemCityObjectBakePolicy> DefaultPolicies =
    [
        Default,
    ];
}
