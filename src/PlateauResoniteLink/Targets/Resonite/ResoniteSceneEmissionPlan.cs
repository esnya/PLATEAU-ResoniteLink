using System;
using System.Collections.Generic;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal readonly record struct GeometryIdentity(string Value);

internal readonly record struct TextureIdentity(string Value);

internal abstract record PlannedGeometryAsset(GeometryIdentity Identity, string MeshAssetSlotName);

internal sealed record PlannedTriangleMeshGeometryAsset(
    GeometryIdentity Identity,
    string MeshAssetSlotName,
    Uri MeshUri)
    : PlannedGeometryAsset(Identity, MeshAssetSlotName);

internal sealed record PlannedTerrainGridGeometryAsset(
    GeometryIdentity Identity,
    string MeshAssetSlotName,
    string TerrainGridAssetSlotName,
    ResoniteTerrainGridGeometry Geometry,
    Uri HeightTextureUri,
    ResoniteFloat2? UvScale = null,
    ResoniteFloat2? UvOffset = null)
    : PlannedGeometryAsset(Identity, MeshAssetSlotName);

internal sealed record PlannedDynamicTerrainGeometryAsset(
    GeometryIdentity Identity,
    string MeshAssetSlotName,
    Uri StaticMeshUri,
    string TerrainGridAssetSlotName,
    ResoniteTerrainGridGeometry GridGeometry,
    Uri HeightTextureUri,
    ResoniteFloat2? UvScale = null,
    ResoniteFloat2? UvOffset = null)
    : PlannedGeometryAsset(Identity, MeshAssetSlotName);

internal sealed record PlannedTextureAsset(
    TextureIdentity Identity,
    Uri AssetUri);

internal abstract record PlannedMaterialAsset;

internal sealed record PlannedReusableMaterialAsset(
    ResoniteComponentLocator Target)
    : PlannedMaterialAsset;

internal sealed record PlannedDedicatedMaterialAsset(
    ResoniteMaterialBinding Material,
    IReadOnlyList<PlannedTextureAsset> Textures,
    bool PreserveDedicatedMaterialSlot)
    : PlannedMaterialAsset;

internal abstract record PlannedRendererMaterialBinding(PlannedMaterialAsset MaterialAsset);

internal sealed record PlannedDirectRendererMaterialBinding(PlannedMaterialAsset MaterialAsset)
    : PlannedRendererMaterialBinding(MaterialAsset);

internal abstract record PlannedMainTextureOverrideRendererMaterialBinding(
    PlannedMaterialAsset MaterialAsset,
    PlannedTextureAsset MainTexture)
    : PlannedRendererMaterialBinding(MaterialAsset);

internal sealed record PlannedAlbedoMainTextureOverrideRendererMaterialBinding(
    PlannedMaterialAsset MaterialAsset,
    PlannedTextureAsset MainTexture)
    : PlannedMainTextureOverrideRendererMaterialBinding(MaterialAsset, MainTexture);

internal sealed record PlannedTerrainMainTextureOverrideRendererMaterialBinding(
    PlannedMaterialAsset MaterialAsset,
    PlannedTextureAsset MainTexture,
    ResoniteComponentLocator? SharedMainTextureComponent = null)
    : PlannedMainTextureOverrideRendererMaterialBinding(MaterialAsset, MainTexture);

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

internal readonly record struct BatchPlanSlotLocator(int Value);

internal readonly record struct BatchPlanComponentLocator(int Value);

internal readonly record struct BatchPlanFieldLocator(int Value);

internal sealed record PlannedTerrainGridMeshBundle(
    BatchPlanComponentLocator ComponentIdentity,
    BatchPlanFieldLocator PointsIdentity,
    Field_int2 Points,
    Field_float2 Size,
    Field_float DisplacementMagnitude,
    PlannedWorldElementReference DisplacementTexture,
    Field_float2? UvScale,
    Field_float2? UvOffset);

internal sealed record PlannedDynamicTerrainMeshBundle(
    PlannedWorldElementReference GridMeshTarget,
    PlannedWorldElementReference StaticMeshTarget)
{
    public PlannedWorldElementReference InitialMeshTarget => GridMeshTarget;
}

internal readonly record struct PlannedSlotTargetReference
{
    private PlannedSlotTargetReference(ResoniteSlotLocator? canonical, BatchPlanSlotLocator? planned)
    {
        Canonical = canonical;
        Planned = planned;
    }

    public ResoniteSlotLocator? Canonical { get; }

    public BatchPlanSlotLocator? Planned { get; }

    public bool IsCanonical => Canonical is not null;

    public bool IsPlanned => Planned is not null;

    public static PlannedSlotTargetReference CanonicalSlot(ResoniteSlotLocator locator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locator.Value);
        return new PlannedSlotTargetReference(locator, null);
    }

    public static PlannedSlotTargetReference PlannedSlot(BatchPlanSlotLocator locator)
    {
        return new PlannedSlotTargetReference(null, locator);
    }
}

internal readonly record struct PlannedWorldElementReference
{
    private PlannedWorldElementReference(
        ResoniteSlotLocator? canonicalSlot,
        ResoniteComponentLocator? canonicalComponent,
        BatchPlanSlotLocator? plannedSlot,
        BatchPlanComponentLocator? plannedComponent,
        BatchPlanFieldLocator? plannedField)
    {
        CanonicalSlot = canonicalSlot;
        CanonicalComponent = canonicalComponent;
        PlannedSlot = plannedSlot;
        PlannedComponent = plannedComponent;
        PlannedField = plannedField;
    }

    public ResoniteSlotLocator? CanonicalSlot { get; }

    public ResoniteComponentLocator? CanonicalComponent { get; }

    public BatchPlanSlotLocator? PlannedSlot { get; }

    public BatchPlanComponentLocator? PlannedComponent { get; }

    public BatchPlanFieldLocator? PlannedField { get; }

    public static PlannedWorldElementReference Canonical(ResoniteSlotLocator locator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locator.Value);
        return new PlannedWorldElementReference(locator, null, null, null, null);
    }

    public static PlannedWorldElementReference Canonical(ResoniteComponentLocator locator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locator.Value);
        return new PlannedWorldElementReference(null, locator, null, null, null);
    }

    public static PlannedWorldElementReference Planned(BatchPlanSlotLocator locator)
    {
        return new PlannedWorldElementReference(null, null, locator, null, null);
    }

    public static PlannedWorldElementReference Planned(BatchPlanComponentLocator locator)
    {
        return new PlannedWorldElementReference(null, null, null, locator, null);
    }

    public static PlannedWorldElementReference Planned(BatchPlanFieldLocator locator)
    {
        return new PlannedWorldElementReference(null, null, null, null, locator);
    }
}

internal abstract record PlannedMember;

internal sealed record PlannedLiteralMember(Member Value) : PlannedMember;

internal sealed record PlannedElementReferenceMember(PlannedWorldElementReference Target) : PlannedMember;

internal sealed record PlannedAddressableFieldMember(BatchPlanFieldLocator Identity, Member Value) : PlannedMember;

internal sealed record PlannedAddressableReferenceMember(
    BatchPlanFieldLocator Identity,
    PlannedWorldElementReference Target)
    : PlannedMember;

internal sealed record PlannedSyncListMember(IReadOnlyList<PlannedMember> Elements) : PlannedMember;

internal sealed record PlannedDriverTargetBundle(
    PlannedAddressableFieldMember Field,
    PlannedElementReferenceMember Target,
    PlannedLiteralMember DefaultValue)
{
    public static PlannedDriverTargetBundle Create(BatchPlanFieldLocator fieldIdentity, Member defaultValue)
    {
        ArgumentNullException.ThrowIfNull(defaultValue);
        return new PlannedDriverTargetBundle(
            new PlannedAddressableFieldMember(fieldIdentity, defaultValue),
            new PlannedElementReferenceMember(PlannedWorldElementReference.Planned(fieldIdentity)),
            new PlannedLiteralMember(CloneDriverDefaultValue(defaultValue)));
    }

    private static Member CloneDriverDefaultValue(Member value)
    {
        return value switch
        {
            Field_bool field => new Field_bool
            {
                Value = field.Value,
            },
            Field_float field => new Field_float
            {
                Value = field.Value,
            },
            _ => throw new InvalidOperationException(
                $"Unsupported planned driver default value type '{value.GetType().Name}'."),
        };
    }
}

internal static class PlannedMembers
{
    public static PlannedMember Literal(Member value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new PlannedLiteralMember(value);
    }

    public static PlannedMember Reference(PlannedWorldElementReference target)
    {
        return new PlannedElementReferenceMember(target);
    }

    public static PlannedMember AddressableField(BatchPlanFieldLocator identity, Member value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new PlannedAddressableFieldMember(identity, value);
    }

    public static PlannedMember AddressableReference(BatchPlanFieldLocator identity, PlannedWorldElementReference target)
    {
        return new PlannedAddressableReferenceMember(identity, target);
    }

    public static PlannedMember List(params PlannedMember[] elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        return new PlannedSyncListMember(elements);
    }

    public static PlannedMember NullReference()
    {
        return Literal(new Reference { TargetID = null });
    }
}

internal sealed record PlannedBatchSlotEmission(
    BatchPlanSlotLocator Identity,
    PlannedSlotTargetReference ParentTarget,
    string SlotName,
    ResoniteFloat3? Position,
    ResoniteFloatQ? Rotation);

internal sealed record PlannedBatchComponentEmission(
    BatchPlanComponentLocator Identity,
    PlannedSlotTargetReference ContainerTarget,
    string ComponentType,
    IReadOnlyDictionary<string, PlannedMember> Members);

internal sealed record PlannedBatchEmission(
    IReadOnlyList<PlannedBatchSlotEmission> SlotEmissions,
    IReadOnlyList<PlannedBatchComponentEmission> ComponentEmissions,
    IReadOnlyList<BatchPlanSlotLocator> SlotResolutionTargets,
    IReadOnlyList<BatchPlanComponentLocator> ComponentResolutionTargets);
