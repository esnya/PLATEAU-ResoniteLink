using System;
using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

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

internal abstract record PlannedRendererMaterialBinding(MaterialIdentity MaterialIdentity);

internal sealed record PlannedDirectRendererMaterialBinding(MaterialIdentity MaterialIdentity)
    : PlannedRendererMaterialBinding(MaterialIdentity);

internal sealed record PlannedMainTextureOverrideRendererMaterialBinding(
    MaterialIdentity MaterialIdentity,
    PlannedTextureAsset MainTexture)
    : PlannedRendererMaterialBinding(MaterialIdentity);

internal sealed record PlannedRenderer(
    GeometryIdentity GeometryIdentity,
    IReadOnlyList<PlannedRendererMaterialBinding> MaterialBindings);

internal sealed record PlannedSceneMaterialPlan(
    IReadOnlyList<PlannedMaterialAsset> MaterialAssets,
    IReadOnlyList<PlannedRendererMaterialBinding> RendererMaterialBindings);

internal sealed record PlannedCollider(
    GeometryIdentity GeometryIdentity,
    bool CollisionEnabled);

internal sealed record PlannedSceneObjectEmission(
    PlannedGeometryAsset GeometryAsset,
    IReadOnlyList<PlannedMaterialAsset> MaterialAssets,
    PlannedRenderer Renderer,
    PlannedCollider Collider);

internal readonly record struct BatchPlanEntityId(string Value);

internal sealed record PlannedBatchSlotEmission(
    BatchPlanEntityId Identity,
    string ParentId,
    string SlotName,
    ResoniteFloat3? Position,
    ResoniteFloatQ? Rotation);

internal sealed record PlannedBatchComponentEmission(
    BatchPlanEntityId Identity,
    string ContainerId,
    string ComponentType,
    IReadOnlyDictionary<string, Member> Members);

internal sealed record PlannedBatchEmission(
    IReadOnlyList<PlannedBatchSlotEmission> SlotEmissions,
    IReadOnlyList<PlannedBatchComponentEmission> ComponentEmissions,
    IReadOnlyList<BatchPlanEntityId> SlotResolutionTargets,
    IReadOnlyList<BatchPlanEntityId> ComponentResolutionTargets);
