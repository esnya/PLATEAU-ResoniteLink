using System;
using System.Collections.Generic;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal abstract record PlannedGeometryAsset(string MeshAssetSlotName);

internal sealed record PlannedTriangleMeshGeometryAsset(
    string MeshAssetSlotName,
    Uri MeshUri)
    : PlannedGeometryAsset(MeshAssetSlotName);

internal sealed record PlannedTerrainGridGeometryAsset(
    string MeshAssetSlotName,
    string TerrainGridAssetSlotName,
    ResoniteTerrainGridGeometry Geometry,
    Uri HeightTextureUri,
    ResoniteFloat2? UvScale = null,
    ResoniteFloat2? UvOffset = null)
    : PlannedGeometryAsset(MeshAssetSlotName);

internal sealed record PlannedDynamicTerrainGeometryAsset(
    string MeshAssetSlotName,
    Uri StaticMeshUri,
    string TerrainGridAssetSlotName,
    ResoniteTerrainGridGeometry GridGeometry,
    Uri HeightTextureUri,
    ResoniteFloat2? UvScale = null,
    ResoniteFloat2? UvOffset = null)
    : PlannedGeometryAsset(MeshAssetSlotName);

internal sealed record PlannedTextureAsset(
    ResoniteSceneMaterialConventions.PlannedTextureRole Role,
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
    IReadOnlyList<PlannedRendererMaterialBinding> MaterialBindings);

internal sealed record PlannedSceneMaterialPlan(
    IReadOnlyList<PlannedMaterialAsset> MaterialAssets,
    IReadOnlyList<PlannedRendererMaterialBinding> RendererMaterialBindings);

internal sealed record PlannedCollider(
    bool CollisionEnabled);

internal sealed record PlannedSceneObjectEmission(
    PlannedGeometryAsset GeometryAsset,
    IReadOnlyList<PlannedMaterialAsset> MaterialAssets,
    PlannedRenderer Renderer,
    PlannedCollider Collider);

internal sealed record PlannedTerrainGridMeshBundle(
    PlannedFieldReference PointsField,
    Field_int2 Points,
    Field_float2 Size,
    Field_float DisplacementMagnitude,
    Field_float MinBoundsY,
    Field_float MaxBoundsY,
    PlannedWorldElementReference DisplacementTexture,
    Field_float2? UvScale,
    Field_float2? UvOffset);

internal sealed class PlannedFieldReference;

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
        Func<PlannedBatchSlotEmission, T> plannedSlot);

    public static PlannedSlotTargetReference CanonicalSlot(ResoniteSlotLocator locator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locator.Value);
        return new CanonicalSlotTarget(locator);
    }

    public static PlannedSlotTargetReference PlannedSlot(PlannedBatchSlotEmission slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        return new BatchSlotTarget(slot);
    }

    internal sealed record CanonicalSlotTarget(ResoniteSlotLocator Locator) : PlannedSlotTargetReference
    {
        public override T Match<T>(
            Func<ResoniteSlotLocator, T> canonicalSlot,
            Func<PlannedBatchSlotEmission, T> plannedSlot)
        {
            return canonicalSlot(Locator);
        }
    }

    internal sealed record BatchSlotTarget(PlannedBatchSlotEmission Slot) : PlannedSlotTargetReference
    {
        public override T Match<T>(
            Func<ResoniteSlotLocator, T> canonicalSlot,
            Func<PlannedBatchSlotEmission, T> plannedSlot)
        {
            return plannedSlot(Slot);
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
        Func<PlannedBatchSlotEmission, T> plannedSlot,
        Func<PlannedBatchComponentEmission, T> plannedComponent,
        Func<PlannedFieldReference, T> plannedField);

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

    public static PlannedWorldElementReference Planned(PlannedBatchSlotEmission slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        return new BatchSlotElement(slot);
    }

    public static PlannedWorldElementReference Planned(PlannedBatchComponentEmission component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return new BatchComponentElement(component);
    }

    public static PlannedWorldElementReference Planned(PlannedFieldReference field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return new BatchFieldElement(field);
    }

    internal sealed record CanonicalSlotElement(ResoniteSlotLocator Locator) : PlannedWorldElementReference
    {
        public override T Match<T>(
            Func<ResoniteSlotLocator, T> canonicalSlot,
            Func<ResoniteComponentLocator, T> canonicalComponent,
            Func<PlannedBatchSlotEmission, T> plannedSlot,
            Func<PlannedBatchComponentEmission, T> plannedComponent,
            Func<PlannedFieldReference, T> plannedField)
        {
            return canonicalSlot(Locator);
        }
    }

    internal sealed record CanonicalComponentElement(ResoniteComponentLocator Locator) : PlannedWorldElementReference
    {
        public override T Match<T>(
            Func<ResoniteSlotLocator, T> canonicalSlot,
            Func<ResoniteComponentLocator, T> canonicalComponent,
            Func<PlannedBatchSlotEmission, T> plannedSlot,
            Func<PlannedBatchComponentEmission, T> plannedComponent,
            Func<PlannedFieldReference, T> plannedField)
        {
            return canonicalComponent(Locator);
        }
    }

    internal sealed record BatchSlotElement(PlannedBatchSlotEmission Slot) : PlannedWorldElementReference
    {
        public override T Match<T>(
            Func<ResoniteSlotLocator, T> canonicalSlot,
            Func<ResoniteComponentLocator, T> canonicalComponent,
            Func<PlannedBatchSlotEmission, T> plannedSlot,
            Func<PlannedBatchComponentEmission, T> plannedComponent,
            Func<PlannedFieldReference, T> plannedField)
        {
            return plannedSlot(Slot);
        }
    }

    internal sealed record BatchComponentElement(PlannedBatchComponentEmission Component) : PlannedWorldElementReference
    {
        public override T Match<T>(
            Func<ResoniteSlotLocator, T> canonicalSlot,
            Func<ResoniteComponentLocator, T> canonicalComponent,
            Func<PlannedBatchSlotEmission, T> plannedSlot,
            Func<PlannedBatchComponentEmission, T> plannedComponent,
            Func<PlannedFieldReference, T> plannedField)
        {
            return plannedComponent(Component);
        }
    }

    internal sealed record BatchFieldElement(PlannedFieldReference Field) : PlannedWorldElementReference
    {
        public override T Match<T>(
            Func<ResoniteSlotLocator, T> canonicalSlot,
            Func<ResoniteComponentLocator, T> canonicalComponent,
            Func<PlannedBatchSlotEmission, T> plannedSlot,
            Func<PlannedBatchComponentEmission, T> plannedComponent,
            Func<PlannedFieldReference, T> plannedField)
        {
            return plannedField(Field);
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
        Func<PlannedFieldReference, PlannedWorldElementReference, T> addressableReference,
        Func<IReadOnlyList<PlannedMember>, T> list);
}

internal sealed record PlannedLiteralMember(Member Value) : PlannedMember
{
    public override T Match<T>(
        Func<Member, T> literal,
        Func<PlannedWorldElementReference, T> reference,
        Func<PlannedAddressableFieldMember, T> addressableField,
        Func<PlannedFieldReference, PlannedWorldElementReference, T> addressableReference,
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
        Func<PlannedFieldReference, PlannedWorldElementReference, T> addressableReference,
        Func<IReadOnlyList<PlannedMember>, T> list)
    {
        return reference(Target);
    }
}

internal abstract record PlannedAddressableFieldMember(PlannedFieldReference Field) : PlannedMember
{
    public abstract Member Value { get; }

    public abstract Member Bind(string fieldId);

    public override T Match<T>(
        Func<Member, T> literal,
        Func<PlannedWorldElementReference, T> reference,
        Func<PlannedAddressableFieldMember, T> addressableField,
        Func<PlannedFieldReference, PlannedWorldElementReference, T> addressableReference,
        Func<IReadOnlyList<PlannedMember>, T> list)
    {
        return addressableField(this);
    }

    internal sealed record Int2(PlannedFieldReference Field, Field_int2 FieldValue)
        : PlannedAddressableFieldMember(Field)
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

    internal sealed record Bool(PlannedFieldReference Field, Field_bool FieldValue)
        : PlannedAddressableFieldMember(Field)
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

    internal sealed record Float(PlannedFieldReference Field, Field_float FieldValue)
        : PlannedAddressableFieldMember(Field)
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
    PlannedFieldReference Field,
    PlannedWorldElementReference Target)
    : PlannedMember
{
    public override T Match<T>(
        Func<Member, T> literal,
        Func<PlannedWorldElementReference, T> reference,
        Func<PlannedAddressableFieldMember, T> addressableField,
        Func<PlannedFieldReference, PlannedWorldElementReference, T> addressableReference,
        Func<IReadOnlyList<PlannedMember>, T> list)
    {
        return addressableReference(Field, Target);
    }
}

internal sealed record PlannedSyncListMember(IReadOnlyList<PlannedMember> Elements) : PlannedMember
{
    public override T Match<T>(
        Func<Member, T> literal,
        Func<PlannedWorldElementReference, T> reference,
        Func<PlannedAddressableFieldMember, T> addressableField,
        Func<PlannedFieldReference, PlannedWorldElementReference, T> addressableReference,
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
    public static PlannedDriverTargetBundle Create(Field_bool defaultValue)
    {
        ArgumentNullException.ThrowIfNull(defaultValue);
        PlannedFieldReference fieldReference = new();
        PlannedAddressableFieldMember field = new PlannedAddressableFieldMember.Bool(fieldReference, defaultValue);
        return new PlannedDriverTargetBundle(
            field,
            new PlannedElementReferenceMember(PlannedWorldElementReference.Planned(fieldReference)),
            new PlannedLiteralMember(new Field_bool
            {
                Value = defaultValue.Value,
            }));
    }

    public static PlannedDriverTargetBundle Create(Field_float defaultValue)
    {
        ArgumentNullException.ThrowIfNull(defaultValue);
        PlannedFieldReference fieldReference = new();
        PlannedAddressableFieldMember field = new PlannedAddressableFieldMember.Float(fieldReference, defaultValue);
        return new PlannedDriverTargetBundle(
            field,
            new PlannedElementReferenceMember(PlannedWorldElementReference.Planned(fieldReference)),
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

    public static PlannedAddressableFieldMember AddressableField(PlannedFieldReference field, Field_int2 value)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);
        return new PlannedAddressableFieldMember.Int2(field, value);
    }

    public static PlannedMember AddressableReference(PlannedFieldReference field, PlannedWorldElementReference target)
    {
        ArgumentNullException.ThrowIfNull(field);
        return new PlannedAddressableReferenceMember(field, target);
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
    PlannedSlotTargetReference ParentTarget,
    string SlotName,
    ResoniteFloat3? Position,
    ResoniteFloatQ? Rotation,
    long? OrderOffset = null);

internal sealed record PlannedBatchComponentEmission(
    PlannedSlotTargetReference ContainerTarget,
    string ComponentType,
    IReadOnlyDictionary<string, PlannedMember> Members);

internal sealed record PlannedBatchEmission
{
    private PlannedBatchEmission(
        IReadOnlyList<PlannedBatchSlotEmission> slotEmissions,
        IReadOnlyList<PlannedBatchComponentEmission> componentEmissions,
        IReadOnlyList<PlannedBatchSlotEmission> slotResolutionTargets,
        IReadOnlyList<PlannedBatchComponentEmission> componentResolutionTargets)
    {
        SlotEmissions = slotEmissions;
        ComponentEmissions = componentEmissions;
        SlotResolutionTargets = slotResolutionTargets;
        ComponentResolutionTargets = componentResolutionTargets;
    }

    public IReadOnlyList<PlannedBatchSlotEmission> SlotEmissions { get; }

    public IReadOnlyList<PlannedBatchComponentEmission> ComponentEmissions { get; }

    public IReadOnlyList<PlannedBatchSlotEmission> SlotResolutionTargets { get; }

    public IReadOnlyList<PlannedBatchComponentEmission> ComponentResolutionTargets { get; }

    public static PlannedBatchEmission Create(
        IReadOnlyList<PlannedBatchSlotEmission> slotEmissions,
        IReadOnlyList<PlannedBatchComponentEmission> componentEmissions,
        IReadOnlyList<PlannedBatchSlotEmission> slotResolutionTargets,
        IReadOnlyList<PlannedBatchComponentEmission> componentResolutionTargets)
    {
        ValidatePlannedReferences(slotEmissions, componentEmissions, slotResolutionTargets, componentResolutionTargets);
        return new PlannedBatchEmission(slotEmissions, componentEmissions, slotResolutionTargets, componentResolutionTargets);
    }

    private static void ValidatePlannedReferences(
        IReadOnlyList<PlannedBatchSlotEmission> slotEmissions,
        IReadOnlyList<PlannedBatchComponentEmission> componentEmissions,
        IReadOnlyList<PlannedBatchSlotEmission> slotResolutionTargets,
        IReadOnlyList<PlannedBatchComponentEmission> componentResolutionTargets)
    {
        HashSet<PlannedBatchSlotEmission> emittedSlots = new(ReferenceEqualityComparer.Instance);
        foreach (PlannedBatchSlotEmission slotEmission in slotEmissions)
        {
            if (slotEmission.ParentTarget is PlannedSlotTargetReference.BatchSlotTarget plannedParent
                && !emittedSlots.Contains(plannedParent.Slot))
            {
                throw new InvalidOperationException("Planned slot parent target must reference an earlier planned slot.");
            }

            emittedSlots.Add(slotEmission);
        }

        foreach (PlannedBatchSlotEmission slotResolutionTarget in slotResolutionTargets)
        {
            if (!emittedSlots.Contains(slotResolutionTarget))
            {
                throw new InvalidOperationException("Planned slot resolution target must reference an emitted planned slot.");
            }
        }

        HashSet<PlannedBatchComponentEmission> emittedComponents = new(ReferenceEqualityComparer.Instance);
        foreach (PlannedBatchComponentEmission componentEmission in componentEmissions)
        {
            if (componentEmission.ContainerTarget is PlannedSlotTargetReference.BatchSlotTarget plannedContainer
                && !emittedSlots.Contains(plannedContainer.Slot))
            {
                throw new InvalidOperationException("Planned component container target must reference an emitted planned slot.");
            }

            foreach (PlannedWorldElementReference target in EnumerateMemberTargets(componentEmission.Members.Values))
            {
                ValidateWorldElementReference(target, emittedSlots, emittedComponents);
            }

            emittedComponents.Add(componentEmission);
        }

        foreach (PlannedBatchComponentEmission componentResolutionTarget in componentResolutionTargets)
        {
            if (!emittedComponents.Contains(componentResolutionTarget))
            {
                throw new InvalidOperationException("Planned component resolution target must reference an emitted planned component.");
            }
        }
    }

    private static void ValidateWorldElementReference(
        PlannedWorldElementReference target,
        HashSet<PlannedBatchSlotEmission> emittedSlots,
        HashSet<PlannedBatchComponentEmission> emittedComponents)
    {
        switch (target)
        {
            case PlannedWorldElementReference.BatchSlotElement plannedSlot
                when !emittedSlots.Contains(plannedSlot.Slot):
                throw new InvalidOperationException("Planned world element target must reference an emitted planned slot.");
            case PlannedWorldElementReference.BatchComponentElement plannedComponent
                when !emittedComponents.Contains(plannedComponent.Component):
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
