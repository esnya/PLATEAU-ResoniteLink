using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

internal enum Lod2AtlasMaterialBakeCategory
{
    AtlasCandidate,
    PreservedCommonMaterial,
    PreservedVertexColor,
    PreservedTextureless,
    PreservedOther,
    Unsupported,
}

internal sealed record Lod2AtlasCityObjectBakePolicy(
    string Name,
    Func<ResoniteConstructionCityObject, bool> CanBufferCityObject,
    bool RequireAtlasCandidateMaterial,
    bool PreserveVertexColorMaterials,
    bool PreserveTexturelessMaterials,
    bool PreserveCommonMaterials,
    bool EnableGridPassThrough,
    int PassThroughGridCellSizeMeters)
{
    public bool CanBuffer(ResoniteConstructionCityObject cityObject)
    {
        return CanBufferCityObject(cityObject);
    }
}

internal static class Lod2AtlasCityObjectBakePolicies
{
    internal static readonly Lod2AtlasCityObjectBakePolicy DefaultBuildingLod2 = new(
        Name: "building-lod2-or-later",
        CanBufferCityObject: cityObject =>
            cityObject.LodLevel >= 2
            && PlateauPackageCatalog.IsBuildingPackage(cityObject.PackageName)
            && cityObject.Geometry is ResoniteTriangleMeshGeometry
            && cityObject.Transform.Rotation is null,
        RequireAtlasCandidateMaterial: true,
        PreserveVertexColorMaterials: false,
        PreserveTexturelessMaterials: false,
        PreserveCommonMaterials: true,
        EnableGridPassThrough: false,
        PassThroughGridCellSizeMeters: 0);

    internal static readonly Lod2AtlasCityObjectBakePolicy NonBuildingLod2OrLaterGridPassThrough = new(
        Name: "non-building-grid-pass-through",
        CanBufferCityObject: cityObject =>
            cityObject.Geometry is ResoniteTriangleMeshGeometry
            && cityObject.Transform.Rotation is null
            && cityObject.LodLevel.HasValue
            && cityObject.LodLevel.Value >= 2
            && !PlateauPackageCatalog.IsBuildingPackage(cityObject.PackageName),
        RequireAtlasCandidateMaterial: false,
        PreserveVertexColorMaterials: true,
        PreserveTexturelessMaterials: true,
        PreserveCommonMaterials: true,
        EnableGridPassThrough: true,
        PassThroughGridCellSizeMeters: 128);

    internal static readonly IReadOnlyList<Lod2AtlasCityObjectBakePolicy> DefaultPolicies =
    [
        DefaultBuildingLod2,
        NonBuildingLod2OrLaterGridPassThrough,
    ];
}
