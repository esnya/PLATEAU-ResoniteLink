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

internal readonly record struct PlannedSlotTargetReference
{
    private PlannedSlotTargetReference(
        PlannedSlotTargetReferenceKind kind,
        ResoniteSlotLocator canonicalSlotLocator,
        BatchPlanSlotLocator plannedSlotLocator)
    {
        Kind = kind;
        CanonicalSlotLocator = canonicalSlotLocator;
        PlannedSlotLocator = plannedSlotLocator;
    }

    public PlannedSlotTargetReferenceKind Kind { get; }

    public ResoniteSlotLocator CanonicalSlotLocator { get; }

    public BatchPlanSlotLocator PlannedSlotLocator { get; }

    public TResult Match<TResult>(
        Func<ResoniteSlotLocator, TResult> canonicalSlot,
        Func<BatchPlanSlotLocator, TResult> plannedSlot)
    {
        return Kind switch
        {
            PlannedSlotTargetReferenceKind.CanonicalSlot => canonicalSlot(CanonicalSlotLocator),
            PlannedSlotTargetReferenceKind.PlannedSlot => plannedSlot(PlannedSlotLocator),
            _ => throw new InvalidOperationException("Planned slot target reference has no assigned target."),
        };
    }

    public static PlannedSlotTargetReference CanonicalSlot(ResoniteSlotLocator locator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locator.Value);
        return new PlannedSlotTargetReference(
            PlannedSlotTargetReferenceKind.CanonicalSlot,
            locator,
            default);
    }

    public static PlannedSlotTargetReference PlannedSlot(BatchPlanSlotLocator locator)
    {
        return new PlannedSlotTargetReference(
            PlannedSlotTargetReferenceKind.PlannedSlot,
            default,
            locator);
    }
}

internal enum PlannedSlotTargetReferenceKind
{
    Unspecified = 0,
    CanonicalSlot = 1,
    PlannedSlot = 2,
}

internal readonly record struct PlannedWorldElementReference
{
    private PlannedWorldElementReference(
        PlannedWorldElementReferenceKind kind,
        ResoniteSlotLocator canonicalSlotLocator,
        ResoniteComponentLocator canonicalComponentLocator,
        BatchPlanSlotLocator plannedSlotLocator,
        BatchPlanComponentLocator plannedComponentLocator,
        BatchPlanFieldLocator plannedFieldLocator)
    {
        Kind = kind;
        CanonicalSlotLocator = canonicalSlotLocator;
        CanonicalComponentLocator = canonicalComponentLocator;
        PlannedSlotLocator = plannedSlotLocator;
        PlannedComponentLocator = plannedComponentLocator;
        PlannedFieldLocator = plannedFieldLocator;
    }

    public PlannedWorldElementReferenceKind Kind { get; }

    public ResoniteSlotLocator CanonicalSlotLocator { get; }

    public ResoniteComponentLocator CanonicalComponentLocator { get; }

    public BatchPlanSlotLocator PlannedSlotLocator { get; }

    public BatchPlanComponentLocator PlannedComponentLocator { get; }

    public BatchPlanFieldLocator PlannedFieldLocator { get; }

    public TResult Match<TResult>(
        Func<ResoniteSlotLocator, TResult> canonicalSlot,
        Func<ResoniteComponentLocator, TResult> canonicalComponent,
        Func<BatchPlanSlotLocator, TResult> plannedSlot,
        Func<BatchPlanComponentLocator, TResult> plannedComponent,
        Func<BatchPlanFieldLocator, TResult> plannedField)
    {
        return Kind switch
        {
            PlannedWorldElementReferenceKind.CanonicalSlot => canonicalSlot(CanonicalSlotLocator),
            PlannedWorldElementReferenceKind.CanonicalComponent => canonicalComponent(CanonicalComponentLocator),
            PlannedWorldElementReferenceKind.PlannedSlot => plannedSlot(PlannedSlotLocator),
            PlannedWorldElementReferenceKind.PlannedComponent => plannedComponent(PlannedComponentLocator),
            PlannedWorldElementReferenceKind.PlannedField => plannedField(PlannedFieldLocator),
            _ => throw new InvalidOperationException("Planned world element reference has no assigned target."),
        };
    }

    public static PlannedWorldElementReference Canonical(ResoniteSlotLocator locator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locator.Value);
        return new PlannedWorldElementReference(
            PlannedWorldElementReferenceKind.CanonicalSlot,
            locator,
            default,
            default,
            default,
            default);
    }

    public static PlannedWorldElementReference Canonical(ResoniteComponentLocator locator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locator.Value);
        return new PlannedWorldElementReference(
            PlannedWorldElementReferenceKind.CanonicalComponent,
            default,
            locator,
            default,
            default,
            default);
    }

    public static PlannedWorldElementReference Planned(BatchPlanSlotLocator locator)
    {
        return new PlannedWorldElementReference(
            PlannedWorldElementReferenceKind.PlannedSlot,
            default,
            default,
            locator,
            default,
            default);
    }

    public static PlannedWorldElementReference Planned(BatchPlanComponentLocator locator)
    {
        return new PlannedWorldElementReference(
            PlannedWorldElementReferenceKind.PlannedComponent,
            default,
            default,
            default,
            locator,
            default);
    }

    public static PlannedWorldElementReference Planned(BatchPlanFieldLocator locator)
    {
        return new PlannedWorldElementReference(
            PlannedWorldElementReferenceKind.PlannedField,
            default,
            default,
            default,
            default,
            locator);
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

internal sealed record PlannedBatchEmission
{
    private PlannedBatchEmission(
        IReadOnlyList<PlannedBatchSlotEmission> slotEmissions,
        IReadOnlyList<PlannedBatchComponentEmission> componentEmissions,
        IReadOnlyList<BatchPlanSlotLocator> slotResolutionTargets,
        IReadOnlyList<BatchPlanComponentLocator> componentResolutionTargets)
    {
        SlotEmissions = slotEmissions;
        ComponentEmissions = componentEmissions;
        SlotResolutionTargets = slotResolutionTargets;
        ComponentResolutionTargets = componentResolutionTargets;
    }

    public IReadOnlyList<PlannedBatchSlotEmission> SlotEmissions { get; }

    public IReadOnlyList<PlannedBatchComponentEmission> ComponentEmissions { get; }

    public IReadOnlyList<BatchPlanSlotLocator> SlotResolutionTargets { get; }

    public IReadOnlyList<BatchPlanComponentLocator> ComponentResolutionTargets { get; }

    public static PlannedBatchEmission Create(
        IReadOnlyList<PlannedBatchSlotEmission> slotEmissions,
        IReadOnlyList<PlannedBatchComponentEmission> componentEmissions,
        IReadOnlyList<BatchPlanSlotLocator> slotResolutionTargets,
        IReadOnlyList<BatchPlanComponentLocator> componentResolutionTargets)
    {
        ValidatePlannedReferences(slotEmissions, componentEmissions, slotResolutionTargets, componentResolutionTargets);
        return new PlannedBatchEmission(slotEmissions, componentEmissions, slotResolutionTargets, componentResolutionTargets);
    }

    private static void ValidatePlannedReferences(
        IReadOnlyList<PlannedBatchSlotEmission> slotEmissions,
        IReadOnlyList<PlannedBatchComponentEmission> componentEmissions,
        IReadOnlyList<BatchPlanSlotLocator> slotResolutionTargets,
        IReadOnlyList<BatchPlanComponentLocator> componentResolutionTargets)
    {
        HashSet<BatchPlanSlotLocator> emittedSlots = [];
        foreach (PlannedBatchSlotEmission slotEmission in slotEmissions)
        {
            if (slotEmission.ParentTarget is PlannedSlotTargetReference.BatchSlotTarget plannedParent
                && !emittedSlots.Contains(plannedParent.Locator))
            {
                throw new InvalidOperationException("Planned slot parent target must reference an earlier planned slot.");
            }

            emittedSlots.Add(slotEmission.Identity);
        }

        foreach (BatchPlanSlotLocator slotResolutionTarget in slotResolutionTargets)
        {
            if (!emittedSlots.Contains(slotResolutionTarget))
            {
                throw new InvalidOperationException("Planned slot resolution target must reference an emitted planned slot.");
            }
        }

        HashSet<BatchPlanComponentLocator> emittedComponents = [];
        foreach (PlannedBatchComponentEmission componentEmission in componentEmissions)
        {
            if (componentEmission.ContainerTarget is PlannedSlotTargetReference.BatchSlotTarget plannedContainer
                && !emittedSlots.Contains(plannedContainer.Locator))
            {
                throw new InvalidOperationException("Planned component container target must reference an emitted planned slot.");
            }

            foreach (PlannedWorldElementReference target in EnumerateMemberTargets(componentEmission.Members.Values))
            {
                ValidateWorldElementReference(target, emittedSlots, emittedComponents);
            }

            emittedComponents.Add(componentEmission.Identity);
        }

        foreach (BatchPlanComponentLocator componentResolutionTarget in componentResolutionTargets)
        {
            if (!emittedComponents.Contains(componentResolutionTarget))
            {
                throw new InvalidOperationException("Planned component resolution target must reference an emitted planned component.");
            }
        }
    }

    private static void ValidateWorldElementReference(
        PlannedWorldElementReference target,
        HashSet<BatchPlanSlotLocator> emittedSlots,
        HashSet<BatchPlanComponentLocator> emittedComponents)
    {
        switch (target)
        {
            case PlannedWorldElementReference.BatchSlotElement plannedSlot
                when !emittedSlots.Contains(plannedSlot.Locator):
                throw new InvalidOperationException("Planned world element target must reference an emitted planned slot.");
            case PlannedWorldElementReference.BatchComponentElement plannedComponent
                when !emittedComponents.Contains(plannedComponent.Locator):
                throw new InvalidOperationException("Planned world element target must reference an earlier planned component.");
        }
    }

    private static IEnumerable<PlannedWorldElementReference> EnumerateMemberTargets(IEnumerable<PlannedMember> members)
    {
        foreach (PlannedMember member in members)
        {
            switch (member)
            {
                case PlannedElementReferenceMember reference:
                    yield return reference.Target;
                    break;
                case PlannedAddressableReferenceMember reference:
                    yield return reference.Target;
                    break;
                case PlannedSyncListMember syncList:
                    foreach (PlannedWorldElementReference nestedTarget in EnumerateMemberTargets(syncList.Elements))
                    {
                        yield return nestedTarget;
                    }

                    break;
            }
        }
    }
}
