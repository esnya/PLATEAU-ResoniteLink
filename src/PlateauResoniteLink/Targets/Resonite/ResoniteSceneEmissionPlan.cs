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

internal abstract record PlannedSlotTargetReference
{
    private PlannedSlotTargetReference()
    {
    }

    public abstract T Match<T>(
        Func<ResoniteSlotLocator, T> canonicalSlot,
        Func<BatchPlanSlotLocator, T> plannedSlot);

    public static PlannedSlotTargetReference CanonicalSlot(ResoniteSlotLocator locator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locator.Value);
        return new CanonicalSlotTarget(locator);
    }

    public static PlannedSlotTargetReference PlannedSlot(BatchPlanSlotLocator locator)
    {
        return new BatchSlotTarget(locator);
    }

    internal sealed record CanonicalSlotTarget(ResoniteSlotLocator Locator) : PlannedSlotTargetReference
    {
        public override T Match<T>(
            Func<ResoniteSlotLocator, T> canonicalSlot,
            Func<BatchPlanSlotLocator, T> plannedSlot)
        {
            return canonicalSlot(Locator);
        }
    }

    internal sealed record BatchSlotTarget(BatchPlanSlotLocator Locator) : PlannedSlotTargetReference
    {
        public override T Match<T>(
            Func<ResoniteSlotLocator, T> canonicalSlot,
            Func<BatchPlanSlotLocator, T> plannedSlot)
        {
            return plannedSlot(Locator);
        }
    }
}

internal abstract record PlannedWorldElementReference
{
    private PlannedWorldElementReference()
    {
    }

    public abstract T Match<T>(
        Func<ResoniteSlotLocator, T> canonicalSlot,
        Func<ResoniteComponentLocator, T> canonicalComponent,
        Func<BatchPlanSlotLocator, T> plannedSlot,
        Func<BatchPlanComponentLocator, T> plannedComponent,
        Func<BatchPlanFieldLocator, T> plannedField);

    public static PlannedWorldElementReference Canonical(ResoniteSlotLocator locator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locator.Value);
        return new CanonicalSlotElement(locator);
    }

    public static PlannedWorldElementReference Canonical(ResoniteComponentLocator locator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locator.Value);
        return new CanonicalComponentElement(locator);
    }

    public static PlannedWorldElementReference Planned(BatchPlanSlotLocator locator)
    {
        return new BatchSlotElement(locator);
    }

    public static PlannedWorldElementReference Planned(BatchPlanComponentLocator locator)
    {
        return new BatchComponentElement(locator);
    }

    public static PlannedWorldElementReference Planned(BatchPlanFieldLocator locator)
    {
        return new BatchFieldElement(locator);
    }

    internal sealed record CanonicalSlotElement(ResoniteSlotLocator Locator) : PlannedWorldElementReference
    {
        public override T Match<T>(
            Func<ResoniteSlotLocator, T> canonicalSlot,
            Func<ResoniteComponentLocator, T> canonicalComponent,
            Func<BatchPlanSlotLocator, T> plannedSlot,
            Func<BatchPlanComponentLocator, T> plannedComponent,
            Func<BatchPlanFieldLocator, T> plannedField)
        {
            return canonicalSlot(Locator);
        }
    }

    internal sealed record CanonicalComponentElement(ResoniteComponentLocator Locator) : PlannedWorldElementReference
    {
        public override T Match<T>(
            Func<ResoniteSlotLocator, T> canonicalSlot,
            Func<ResoniteComponentLocator, T> canonicalComponent,
            Func<BatchPlanSlotLocator, T> plannedSlot,
            Func<BatchPlanComponentLocator, T> plannedComponent,
            Func<BatchPlanFieldLocator, T> plannedField)
        {
            return canonicalComponent(Locator);
        }
    }

    internal sealed record BatchSlotElement(BatchPlanSlotLocator Locator) : PlannedWorldElementReference
    {
        public override T Match<T>(
            Func<ResoniteSlotLocator, T> canonicalSlot,
            Func<ResoniteComponentLocator, T> canonicalComponent,
            Func<BatchPlanSlotLocator, T> plannedSlot,
            Func<BatchPlanComponentLocator, T> plannedComponent,
            Func<BatchPlanFieldLocator, T> plannedField)
        {
            return plannedSlot(Locator);
        }
    }

    internal sealed record BatchComponentElement(BatchPlanComponentLocator Locator) : PlannedWorldElementReference
    {
        public override T Match<T>(
            Func<ResoniteSlotLocator, T> canonicalSlot,
            Func<ResoniteComponentLocator, T> canonicalComponent,
            Func<BatchPlanSlotLocator, T> plannedSlot,
            Func<BatchPlanComponentLocator, T> plannedComponent,
            Func<BatchPlanFieldLocator, T> plannedField)
        {
            return plannedComponent(Locator);
        }
    }

    internal sealed record BatchFieldElement(BatchPlanFieldLocator Locator) : PlannedWorldElementReference
    {
        public override T Match<T>(
            Func<ResoniteSlotLocator, T> canonicalSlot,
            Func<ResoniteComponentLocator, T> canonicalComponent,
            Func<BatchPlanSlotLocator, T> plannedSlot,
            Func<BatchPlanComponentLocator, T> plannedComponent,
            Func<BatchPlanFieldLocator, T> plannedField)
        {
            return plannedField(Locator);
        }
    }
}

internal abstract record PlannedMember
{
    private protected PlannedMember()
    {
    }

    public abstract T Match<T>(
        Func<Member, T> literal,
        Func<PlannedWorldElementReference, T> reference,
        Func<PlannedAddressableFieldMember, T> addressableField,
        Func<BatchPlanFieldLocator, PlannedWorldElementReference, T> addressableReference,
        Func<IReadOnlyList<PlannedMember>, T> list);
}

internal sealed record PlannedLiteralMember(Member Value) : PlannedMember
{
    public override T Match<T>(
        Func<Member, T> literal,
        Func<PlannedWorldElementReference, T> reference,
        Func<PlannedAddressableFieldMember, T> addressableField,
        Func<BatchPlanFieldLocator, PlannedWorldElementReference, T> addressableReference,
        Func<IReadOnlyList<PlannedMember>, T> list)
    {
        return literal(Value);
    }
}

internal sealed record PlannedElementReferenceMember(PlannedWorldElementReference Target) : PlannedMember
{
    public override T Match<T>(
        Func<Member, T> literal,
        Func<PlannedWorldElementReference, T> reference,
        Func<PlannedAddressableFieldMember, T> addressableField,
        Func<BatchPlanFieldLocator, PlannedWorldElementReference, T> addressableReference,
        Func<IReadOnlyList<PlannedMember>, T> list)
    {
        return reference(Target);
    }
}

internal abstract record PlannedAddressableFieldMember(BatchPlanFieldLocator Identity) : PlannedMember
{
    public abstract Member Value { get; }

    public abstract Member Bind(string fieldId);

    public override T Match<T>(
        Func<Member, T> literal,
        Func<PlannedWorldElementReference, T> reference,
        Func<PlannedAddressableFieldMember, T> addressableField,
        Func<BatchPlanFieldLocator, PlannedWorldElementReference, T> addressableReference,
        Func<IReadOnlyList<PlannedMember>, T> list)
    {
        return addressableField(this);
    }

    internal sealed record Int2(BatchPlanFieldLocator Identity, Field_int2 FieldValue)
        : PlannedAddressableFieldMember(Identity)
    {
        public override Member Value => FieldValue;

        public override Member Bind(string fieldId)
        {
            return new Field_int2
            {
                ID = fieldId,
                Value = FieldValue.Value,
            };
        }
    }

    internal sealed record Bool(BatchPlanFieldLocator Identity, Field_bool FieldValue)
        : PlannedAddressableFieldMember(Identity)
    {
        public override Member Value => FieldValue;

        public override Member Bind(string fieldId)
        {
            return new Field_bool
            {
                ID = fieldId,
                Value = FieldValue.Value,
            };
        }
    }

    internal sealed record Float(BatchPlanFieldLocator Identity, Field_float FieldValue)
        : PlannedAddressableFieldMember(Identity)
    {
        public override Member Value => FieldValue;

        public override Member Bind(string fieldId)
        {
            return new Field_float
            {
                ID = fieldId,
                Value = FieldValue.Value,
            };
        }
    }
}

internal sealed record PlannedAddressableReferenceMember(
    BatchPlanFieldLocator Identity,
    PlannedWorldElementReference Target)
    : PlannedMember
{
    public override T Match<T>(
        Func<Member, T> literal,
        Func<PlannedWorldElementReference, T> reference,
        Func<PlannedAddressableFieldMember, T> addressableField,
        Func<BatchPlanFieldLocator, PlannedWorldElementReference, T> addressableReference,
        Func<IReadOnlyList<PlannedMember>, T> list)
    {
        return addressableReference(Identity, Target);
    }
}

internal sealed record PlannedSyncListMember(IReadOnlyList<PlannedMember> Elements) : PlannedMember
{
    public override T Match<T>(
        Func<Member, T> literal,
        Func<PlannedWorldElementReference, T> reference,
        Func<PlannedAddressableFieldMember, T> addressableField,
        Func<BatchPlanFieldLocator, PlannedWorldElementReference, T> addressableReference,
        Func<IReadOnlyList<PlannedMember>, T> list)
    {
        return list(Elements);
    }
}

internal sealed record PlannedDriverTargetBundle(
    PlannedAddressableFieldMember Field,
    PlannedElementReferenceMember Target,
    PlannedLiteralMember DefaultValue)
{
    public static PlannedDriverTargetBundle Create(BatchPlanFieldLocator fieldIdentity, Field_bool defaultValue)
    {
        ArgumentNullException.ThrowIfNull(defaultValue);
        return new PlannedDriverTargetBundle(
            new PlannedAddressableFieldMember.Bool(fieldIdentity, defaultValue),
            new PlannedElementReferenceMember(PlannedWorldElementReference.Planned(fieldIdentity)),
            new PlannedLiteralMember(new Field_bool
            {
                Value = defaultValue.Value,
            }));
    }

    public static PlannedDriverTargetBundle Create(BatchPlanFieldLocator fieldIdentity, Field_float defaultValue)
    {
        ArgumentNullException.ThrowIfNull(defaultValue);
        return new PlannedDriverTargetBundle(
            new PlannedAddressableFieldMember.Float(fieldIdentity, defaultValue),
            new PlannedElementReferenceMember(PlannedWorldElementReference.Planned(fieldIdentity)),
            new PlannedLiteralMember(new Field_float
            {
                Value = defaultValue.Value,
            }));
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

    public static PlannedMember AddressableField(BatchPlanFieldLocator identity, Field_int2 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new PlannedAddressableFieldMember.Int2(identity, value);
    }

    public static PlannedMember AddressableField(BatchPlanFieldLocator identity, Field_bool value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new PlannedAddressableFieldMember.Bool(identity, value);
    }

    public static PlannedMember AddressableField(BatchPlanFieldLocator identity, Field_float value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new PlannedAddressableFieldMember.Float(identity, value);
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
