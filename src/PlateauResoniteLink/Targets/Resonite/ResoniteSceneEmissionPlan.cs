using System;
using System.Collections.Generic;
using System.Linq;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal abstract record PlannedGeometryAsset;

internal sealed record PlannedTriangleMeshGeometryAsset(
    Uri MeshUri)
    : PlannedGeometryAsset;

internal sealed record PlannedTerrainGridGeometryAsset(
    ResoniteTerrainGridGeometry Geometry,
    Uri HeightTextureUri,
    ResoniteFloat2? UvScale = null,
    ResoniteFloat2? UvOffset = null)
    : PlannedGeometryAsset;

internal sealed record PlannedDynamicTerrainGeometryAsset(
    Uri StaticMeshUri,
    ResoniteTerrainGridGeometry GridGeometry,
    Uri HeightTextureUri,
    ResoniteFloat2? UvScale = null,
    ResoniteFloat2? UvOffset = null)
    : PlannedGeometryAsset;

internal sealed record PlannedTextureAsset(
    ResoniteSceneMaterialConventions.PlannedTextureRole Role,
    Uri AssetUri);

internal abstract record PlannedMaterialAsset;

internal sealed record PlannedReusableMaterialAsset(
    ResoniteComponentLocator Target)
    : PlannedMaterialAsset;

internal sealed record PlannedDedicatedMaterialAsset(
    ResoniteMaterialBinding Material,
    IReadOnlyList<PlannedTextureAsset> Textures)
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

internal sealed record PlannedSceneMaterialPlan(
    IReadOnlyList<PlannedMaterialAsset> MaterialAssets,
    IReadOnlyList<PlannedRendererMaterialBinding> RendererMaterialBindings);

internal sealed record PlannedTerrainGridMeshBundle(
    PlannedFieldReference PointsField,
    Field_int2 Points,
    Field_float2 Size,
    Field_float DisplacementMagnitude,
    Field_float MinBoundsY,
    Field_float MaxBoundsY,
    PlannedWorldElementReference DisplacementTexture,
    Field_float2? UvScale,
    Field_float2? UvOffset)
{
    public static PlannedTerrainGridMeshBundle Create(
        ResoniteTerrainGridGeometry geometry,
        PlannedWorldElementReference displacementTexture,
        ResoniteFloat2? uvScale,
        ResoniteFloat2? uvOffset)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(displacementTexture);

        Field_float displacement = new()
        {
            Value = -1.0f,
        };
        float firstBoundsY = (float)(geometry.MinHeight * displacement.Value);
        float secondBoundsY = (float)(geometry.MaxHeight * displacement.Value);
        return new PlannedTerrainGridMeshBundle(
            new PlannedFieldReference(),
            new Field_int2
            {
                Value = new int2
                {
                    x = geometry.Width,
                    y = geometry.Height,
                },
            },
            new Field_float2
            {
                Value = new float2
                {
                    x = (float)geometry.Size.X,
                    y = (float)geometry.Size.Y,
                },
            },
            displacement,
            new Field_float
            {
                Value = Math.Min(firstBoundsY, secondBoundsY),
            },
            new Field_float
            {
                Value = Math.Max(firstBoundsY, secondBoundsY),
            },
            displacementTexture,
            ToField(uvScale),
            ToField(uvOffset));
    }

    public Field_BoundingBox CreateOverriddenBoundingBox()
    {
        float halfWidth = Math.Abs(Size.Value.x) / 2.0f;
        float halfDepth = Math.Abs(Size.Value.y) / 2.0f;
        return new Field_BoundingBox
        {
            Value = new BoundingBox
            {
                min = new float3
                {
                    x = -halfWidth,
                    y = MinBoundsY.Value,
                    z = -halfDepth,
                },
                max = new float3
                {
                    x = halfWidth,
                    y = MaxBoundsY.Value,
                    z = halfDepth,
                },
            },
        };
    }

    private static Field_float2? ToField(ResoniteFloat2? value)
    {
        return value is null
            ? null
            : new Field_float2
            {
                Value = new float2
                {
                    x = (float)value.X,
                    y = (float)value.Y,
                },
            };
    }
}

internal sealed record PlannedMainTexturePropertyBlockOverride(
    PlannedMember PropertyBlock,
    PlannedBatchComponentEmission? TextureComponent,
    PlannedBatchComponentEmission? PropertyBlockComponent)
{
    public static PlannedMainTexturePropertyBlockOverride Create(
        PlannedSlotTargetReference textureContainerTarget,
        PlannedSlotTargetReference propertyBlockContainerTarget,
        PlannedMainTextureOverrideRendererMaterialBinding binding)
    {
        ArgumentNullException.ThrowIfNull(textureContainerTarget);
        ArgumentNullException.ThrowIfNull(propertyBlockContainerTarget);
        ArgumentNullException.ThrowIfNull(binding);

        if (binding is PlannedTerrainMainTextureOverrideRendererMaterialBinding
            {
                SharedMainTexturePropertyBlockComponent: { } sharedMainTexturePropertyBlockComponent,
            })
        {
            return new PlannedMainTexturePropertyBlockOverride(
                PlannedMembers.Reference(PlannedWorldElementReference.Canonical(sharedMainTexturePropertyBlockComponent)),
                null,
                null);
        }

        PlannedWorldElementReference? sharedTextureTarget = binding is PlannedTerrainMainTextureOverrideRendererMaterialBinding
        {
            SharedMainTextureComponent: { } sharedMainTextureComponent,
        }
            ? PlannedWorldElementReference.Canonical(sharedMainTextureComponent)
            : null;
        ResoniteSceneMaterialConventions.TextureMemberRole textureRole = binding switch
        {
            PlannedAlbedoMainTextureOverrideRendererMaterialBinding =>
                ResoniteSceneMaterialConventions.TextureMemberRole.Albedo,
            PlannedTerrainMainTextureOverrideRendererMaterialBinding =>
                ResoniteSceneMaterialConventions.TextureMemberRole.TerrainMainTextureOverride,
            _ => throw new InvalidOperationException(
                $"Unsupported planned main texture override binding type '{binding.GetType().Name}'."),
        };
        PlannedBatchComponentEmission? textureComponent = sharedTextureTarget is null
            ? CreateTextureComponent(
                textureContainerTarget,
                binding.MainTexture.AssetUri,
                textureRole)
            : null;
        PlannedWorldElementReference textureTarget = sharedTextureTarget
            ?? PlannedWorldElementReference.Planned(textureComponent ?? throw new InvalidOperationException("Main texture property block did not create a texture component."));
        PlannedBatchComponentEmission propertyBlockComponent = new(
            propertyBlockContainerTarget,
            "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock",
            new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
            {
                ["Texture"] = PlannedMembers.Reference(textureTarget),
            });
        return new PlannedMainTexturePropertyBlockOverride(
            PlannedMembers.Reference(PlannedWorldElementReference.Planned(propertyBlockComponent)),
            textureComponent,
            propertyBlockComponent);
    }

    private static PlannedBatchComponentEmission CreateTextureComponent(
        PlannedSlotTargetReference containerTarget,
        Uri textureUri,
        ResoniteSceneMaterialConventions.TextureMemberRole textureRole)
    {
        return new PlannedBatchComponentEmission(
            containerTarget,
            "[FrooxEngine]FrooxEngine.StaticTexture2D",
            ResoniteSceneMaterialConventions.CreateTextureMembers(textureUri, textureRole)
                .ToDictionary(static pair => pair.Key, static pair => PlannedMembers.Literal(pair.Value), StringComparer.Ordinal));
    }
}

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

    internal sealed record CanonicalSlotTarget(ResoniteSlotLocator Locator) : PlannedSlotTargetReference;

    internal sealed record BatchSlotTarget(PlannedBatchSlotEmission Slot) : PlannedSlotTargetReference;
}

internal abstract record PlannedWorldElementReference
{
    private PlannedWorldElementReference()
    {
    }

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

    internal sealed record CanonicalSlotElement(ResoniteSlotLocator Locator) : PlannedWorldElementReference;

    internal sealed record CanonicalComponentElement(ResoniteComponentLocator Locator) : PlannedWorldElementReference;

    internal sealed record BatchSlotElement(PlannedBatchSlotEmission Slot) : PlannedWorldElementReference;

    internal sealed record BatchComponentElement(PlannedBatchComponentEmission Component) : PlannedWorldElementReference;

    internal sealed record BatchFieldElement(PlannedFieldReference Field) : PlannedWorldElementReference;
}

internal abstract record PlannedMember
{
    private protected PlannedMember()
    {
    }
}

internal sealed record PlannedLiteralMember(Member Value) : PlannedMember;

internal sealed record PlannedElementReferenceMember(PlannedWorldElementReference Target) : PlannedMember;

internal abstract record PlannedAddressableFieldMember(PlannedFieldReference Field) : PlannedMember
{
    public abstract Member Value { get; }

    public abstract Member Bind(string fieldId);

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
    : PlannedMember;

internal sealed record PlannedSyncListMember(IReadOnlyList<PlannedMember> Elements) : PlannedMember;

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
    long? OrderOffset = null)
{
    public static PlannedBatchSlotEmission Presentation(
        ResoniteObjectSlotHierarchy objectSlots)
    {
        ArgumentNullException.ThrowIfNull(objectSlots);

        return new PlannedBatchSlotEmission(
            PlannedSlotTargetReference.CanonicalSlot(objectSlots.LodSlot.Locator),
            objectSlots.CityObjectSlotName,
            objectSlots.CityObjectLocalPosition,
            objectSlots.CityObjectRotation,
            objectSlots.CityObjectOrderOffset);
    }

}

internal sealed record PlannedBatchComponentEmission(
    PlannedSlotTargetReference ContainerTarget,
    string ComponentType,
    IReadOnlyDictionary<string, PlannedMember> Members);

internal sealed record PlannedBatchEmission
{
    private PlannedBatchEmission(
        IReadOnlyList<PlannedBatchSlotEmission> slotEmissions,
        IReadOnlyList<PlannedBatchComponentEmission> componentEmissions)
    {
        SlotEmissions = slotEmissions;
        ComponentEmissions = componentEmissions;
    }

    public IReadOnlyList<PlannedBatchSlotEmission> SlotEmissions { get; }

    public IReadOnlyList<PlannedBatchComponentEmission> ComponentEmissions { get; }

    public static PlannedBatchEmission Create(
        IReadOnlyList<PlannedBatchSlotEmission> slotEmissions,
        IReadOnlyList<PlannedBatchComponentEmission> componentEmissions)
    {
        ValidatePlannedReferences(slotEmissions, componentEmissions);
        return new PlannedBatchEmission(slotEmissions, componentEmissions);
    }

    private static void ValidatePlannedReferences(
        IReadOnlyList<PlannedBatchSlotEmission> slotEmissions,
        IReadOnlyList<PlannedBatchComponentEmission> componentEmissions)
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
