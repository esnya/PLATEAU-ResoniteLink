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
    bool PreserveDedicatedMaterialSlot,
    string? DedicatedMaterialSlotName = null)
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
    ResoniteComponentLocator? SharedMainTextureComponent = null,
    ResoniteComponentLocator? SharedMainTexturePropertyBlockComponent = null)
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

internal enum PlannedSlotTargetReferenceKind
{
    Unspecified = 0,
    Canonical = 1,
    Planned = 2,
}

internal readonly record struct PlannedSlotTargetReference
{
    private PlannedSlotTargetReference(
        PlannedSlotTargetReferenceKind kind,
        ResoniteSlotLocator canonicalSlot,
        BatchPlanSlotLocator plannedSlot)
    {
        Kind = kind;
        CanonicalLocator = canonicalSlot;
        PlannedLocator = plannedSlot;
    }

    public PlannedSlotTargetReferenceKind Kind { get; }

    public ResoniteSlotLocator CanonicalLocator { get; }

    public BatchPlanSlotLocator PlannedLocator { get; }

    public static PlannedSlotTargetReference CanonicalSlot(ResoniteSlotLocator locator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locator.Value);
        return new PlannedSlotTargetReference(PlannedSlotTargetReferenceKind.Canonical, locator, default);
    }

    public static PlannedSlotTargetReference PlannedSlot(BatchPlanSlotLocator locator)
    {
        return new PlannedSlotTargetReference(PlannedSlotTargetReferenceKind.Planned, default, locator);
    }
}

internal enum PlannedWorldElementReferenceKind
{
    Unspecified = 0,
    CanonicalSlot = 1,
    CanonicalComponent = 2,
    PlannedSlot = 3,
    PlannedComponent = 4,
    PlannedField = 5,
}

internal readonly record struct PlannedWorldElementReference
{
    private PlannedWorldElementReference(
        PlannedWorldElementReferenceKind kind,
        ResoniteSlotLocator canonicalSlot,
        ResoniteComponentLocator canonicalComponent,
        BatchPlanSlotLocator plannedSlot,
        BatchPlanComponentLocator plannedComponent,
        BatchPlanFieldLocator plannedField)
    {
        Kind = kind;
        CanonicalSlot = canonicalSlot;
        CanonicalComponent = canonicalComponent;
        PlannedSlot = plannedSlot;
        PlannedComponent = plannedComponent;
        PlannedField = plannedField;
    }

    public PlannedWorldElementReferenceKind Kind { get; }

    public ResoniteSlotLocator CanonicalSlot { get; }

    public ResoniteComponentLocator CanonicalComponent { get; }

    public BatchPlanSlotLocator PlannedSlot { get; }

    public BatchPlanComponentLocator PlannedComponent { get; }

    public BatchPlanFieldLocator PlannedField { get; }

    public static PlannedWorldElementReference Canonical(ResoniteSlotLocator locator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locator.Value);
        return new PlannedWorldElementReference(PlannedWorldElementReferenceKind.CanonicalSlot, locator, default, default, default, default);
    }

    public static PlannedWorldElementReference Canonical(ResoniteComponentLocator locator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locator.Value);
        return new PlannedWorldElementReference(PlannedWorldElementReferenceKind.CanonicalComponent, default, locator, default, default, default);
    }

    public static PlannedWorldElementReference Planned(BatchPlanSlotLocator locator)
    {
        return new PlannedWorldElementReference(PlannedWorldElementReferenceKind.PlannedSlot, default, default, locator, default, default);
    }

    public static PlannedWorldElementReference Planned(BatchPlanComponentLocator locator)
    {
        return new PlannedWorldElementReference(PlannedWorldElementReferenceKind.PlannedComponent, default, default, default, locator, default);
    }

    public static PlannedWorldElementReference Planned(BatchPlanFieldLocator locator)
    {
        return new PlannedWorldElementReference(PlannedWorldElementReferenceKind.PlannedField, default, default, default, default, locator);
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
