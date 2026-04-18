using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Targets.Resonite;

internal readonly record struct GeometryIdentity(string Value);

internal readonly record struct TextureIdentity(string Value);

internal readonly record struct MaterialIdentity(string Value);

internal abstract record PlannedGeometryAsset(GeometryIdentity Identity, string MeshAssetSlotName);

internal sealed record PlannedTriangleMeshGeometryAsset(
    GeometryIdentity Identity,
    string MeshAssetSlotName,
    Uri MeshUri)
    : PlannedGeometryAsset(Identity, MeshAssetSlotName);

internal sealed record PlannedHeightMapGridGeometryAsset(
    GeometryIdentity Identity,
    string MeshAssetSlotName,
    string HeightMapAssetSlotName,
    ResoniteHeightMapGridGeometry Geometry,
    Uri HeightTextureUri)
    : PlannedGeometryAsset(Identity, MeshAssetSlotName);

internal sealed record PlannedTextureAsset(
    TextureIdentity Identity,
    Uri AssetUri);

internal abstract record PlannedMaterialAsset(MaterialIdentity Identity);

internal sealed record PlannedReusableMaterialAsset(
    MaterialIdentity Identity,
    string TargetId)
    : PlannedMaterialAsset(Identity);

internal sealed record PlannedDedicatedMaterialAsset(
    MaterialIdentity Identity,
    ResoniteMaterialBinding Material,
    IReadOnlyList<PlannedTextureAsset> Textures,
    bool PreserveDedicatedMaterialSlot)
    : PlannedMaterialAsset(Identity);

internal sealed record PlannedRenderer(
    GeometryIdentity GeometryIdentity,
    IReadOnlyList<MaterialIdentity> MaterialIdentities);

internal sealed record PlannedCollider(
    GeometryIdentity GeometryIdentity,
    bool CollisionEnabled);

internal sealed record PlannedSceneObjectEmission(
    PlannedGeometryAsset GeometryAsset,
    IReadOnlyList<PlannedMaterialAsset> MaterialAssets,
    PlannedRenderer Renderer,
    PlannedCollider Collider);
