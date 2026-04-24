using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Execution;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResoniteSceneBatchEmissionPlanningTests
{
    private static readonly IResoniteBatchEmissionPlanner Planner = new ResoniteBatchEmissionPlanner();

    [Fact]
    public void CreatePlannedBatchEmission_CreatesTerrainGridPlanWithPlannedTextureReference()
    {
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots = new(
            new CreatedSlot(new ResoniteSlotLocator("asset-lod-slot"), "Asset LOD"),
            new CreatedSlot(new ResoniteSlotLocator("lod-slot"), "LOD"),
            "Terrain Grid Object",
            new PlateauResoniteLink.Targets.Resonite.ResoniteFloat3(1.0, 2.0, 3.0),
            null);
        PlannedSceneObjectEmission emissionPlan = new(
            new PlannedTerrainGridGeometryAsset(
                new GeometryIdentity("geom"),
                "Terrain Grid Object",
                "Terrain Grid Object_terrain-grid",
                new ResoniteTerrainGridGeometry(
                    Width: 2,
                    Height: 3,
                    Size: new PlateauResoniteLink.Targets.Resonite.ResoniteFloat2(10.0, 20.0),
                    MinHeight: 0.0,
                    MaxHeight: 6.0,
                    HeightSamples: [0.0, 1.0, 2.0, 3.0, 4.0, 5.0]),
                new Uri("resdb:///texture/height")),
            [],
            new PlannedRenderer(
                new GeometryIdentity("geom"),
                []),
            new PlannedCollider(
                new GeometryIdentity("geom"),
                true));

        PlannedBatchEmission batchPlan = Planner.Create(objectSlots, emissionPlan);

        PlannedBatchSlotEmission meshAssetSlot = Assert.Single(
            batchPlan.SlotEmissions,
            static slot => string.Equals(slot.SlotName, "Terrain Grid Object", StringComparison.Ordinal)
                && slot.ParentTarget.Canonical == new ResoniteSlotLocator("asset-lod-slot"));
        PlannedBatchSlotEmission heightMapSlot = Assert.Single(
            batchPlan.SlotEmissions,
            static slot => string.Equals(slot.SlotName, "Terrain Grid Object_terrain-grid", StringComparison.Ordinal));
        PlannedBatchComponentEmission heightTexture = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal));
        PlannedBatchComponentEmission gridMesh = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.GridMesh", StringComparison.Ordinal));

        Assert.Equal(new ResoniteSlotLocator("asset-lod-slot"), AssertCanonical(meshAssetSlot.ParentTarget));
        Assert.False(meshAssetSlot.ParentTarget.IsPlanned);
        Assert.Equal(new ResoniteSlotLocator("asset-lod-slot"), AssertCanonical(heightMapSlot.ParentTarget));
        Assert.False(heightMapSlot.ParentTarget.IsPlanned);
        Assert.Equal(heightMapSlot.Identity, AssertPlanned(heightTexture.ContainerTarget));
        Assert.True(heightTexture.ContainerTarget.IsPlanned);
        Assert.Equal("Clamp", Assert.IsType<Field_Enum>(ToMember(heightTexture.Members["WrapModeU"])).Value);
        Assert.Equal("Clamp", Assert.IsType<Field_Enum>(ToMember(heightTexture.Members["WrapModeV"])).Value);
        Assert.Equal("Point", Assert.IsType<Field_Nullable_Enum>(ToMember(heightTexture.Members["FilterMode"])).Value);
        Assert.False(Assert.IsType<Field_bool>(ToMember(heightTexture.Members["MipMaps"])).Value);
        Reference displacementTexture = Assert.IsType<Reference>(ToMember(gridMesh.Members["DisplacementTexture"]));
        Assert.Equal(ToPlannedTargetId(heightTexture.Identity), displacementTexture.TargetID);
    }

    [Fact]
    public void CreatePlannedBatchEmission_CarriesTerrainGridUvMembers()
    {
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots = new(
            new CreatedSlot(new ResoniteSlotLocator("asset-lod-slot"), "Asset LOD"),
            new CreatedSlot(new ResoniteSlotLocator("lod-slot"), "LOD"),
            "Terrain Grid Object",
            new PlateauResoniteLink.Targets.Resonite.ResoniteFloat3(1.0, 2.0, 3.0),
            null);
        PlannedSceneObjectEmission emissionPlan = new(
            new PlannedTerrainGridGeometryAsset(
                new GeometryIdentity("geom"),
                "Terrain Grid Object",
                "Terrain Grid Object_terrain-grid",
                new ResoniteTerrainGridGeometry(
                    Width: 2,
                    Height: 3,
                    Size: new PlateauResoniteLink.Targets.Resonite.ResoniteFloat2(10.0, 20.0),
                    MinHeight: 0.0,
                    MaxHeight: 6.0,
                    HeightSamples: [0.0, 1.0, 2.0, 3.0, 4.0, 5.0]),
                new Uri("resdb:///texture/height"),
                new PlateauResoniteLink.Targets.Resonite.ResoniteFloat2(0.4, 0.25),
                new PlateauResoniteLink.Targets.Resonite.ResoniteFloat2(0.2, 0.125)),
            [],
            new PlannedRenderer(
                new GeometryIdentity("geom"),
                []),
            new PlannedCollider(
                new GeometryIdentity("geom"),
                true));

        PlannedBatchEmission batchPlan = Planner.Create(objectSlots, emissionPlan);

        PlannedBatchComponentEmission gridMesh = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.GridMesh", StringComparison.Ordinal));
        Field_float2 uvScale = Assert.IsType<Field_float2>(ToMember(gridMesh.Members["UVScale"]));
        Field_float2 uvOffset = Assert.IsType<Field_float2>(ToMember(gridMesh.Members["UVOffset"]));

        Assert.Equal(0.4f, uvScale.Value.x, 6);
        Assert.Equal(0.25f, uvScale.Value.y, 6);
        Assert.Equal(0.2f, uvOffset.Value.x, 6);
        Assert.Equal(0.125f, uvOffset.Value.y, 6);
    }

    [Fact]
    public void CreatePlannedBatchEmission_UsesReusableTargetsAndPlansDedicatedMaterialComponents()
    {
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots = new(
            new CreatedSlot(new ResoniteSlotLocator("asset-lod-slot"), "Asset LOD"),
            new CreatedSlot(new ResoniteSlotLocator("lod-slot"), "LOD"),
            "Triangle Object",
            new PlateauResoniteLink.Targets.Resonite.ResoniteFloat3(0.0, 0.0, 0.0),
            null);
        PlannedDedicatedMaterialAsset dedicatedMaterial = new(
            new MaterialIdentity("dedicated"),
            new ResoniteMaterialBinding(
                MaterialKey: "dedicated-material",
                BaseColor: new PlateauResoniteLink.Targets.Resonite.ResoniteColor(1.0, 1.0, 1.0, 1.0),
                MaterialType: ResoniteMaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                Projection: ResoniteMaterialProjection.Uv,
                DepthOffset: null,
                SubmeshIndices: [0]),
            [new PlannedTextureAsset(new TextureIdentity("albedo"), new Uri("resdb:///texture/albedo"))],
            PreserveDedicatedMaterialSlot: true);
        PlannedSceneObjectEmission emissionPlan = new(
            new PlannedTriangleMeshGeometryAsset(
                new GeometryIdentity("geom"),
                "Triangle Object",
                new Uri("resdb:///mesh/triangle")),
            [
                new PlannedReusableMaterialAsset(new MaterialIdentity("reusable"), new ResoniteComponentLocator("existing-material-id")),
                dedicatedMaterial,
            ],
            new PlannedRenderer(
                new GeometryIdentity("geom"),
                [
                    new PlannedDirectRendererMaterialBinding(new MaterialIdentity("reusable")),
                    new PlannedDirectRendererMaterialBinding(dedicatedMaterial.Identity),
                ]),
            new PlannedCollider(
                new GeometryIdentity("geom"),
                false));

        PlannedBatchEmission batchPlan = Planner.Create(objectSlots, emissionPlan);

        PlannedBatchComponentEmission meshRenderer = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal));
        SyncList materials = Assert.IsType<SyncList>(ToMember(meshRenderer.Members["Materials"]));
        Reference reusableMaterialReference = Assert.IsType<Reference>(materials.Elements[0]);
        Reference dedicatedMaterialReference = Assert.IsType<Reference>(materials.Elements[1]);
        PlannedBatchSlotEmission dedicatedMaterialSlot = Assert.Single(
            batchPlan.SlotEmissions,
            slot => slot.ParentTarget.Planned == FindAssetSlotIdentity(batchPlan, "Triangle Object")
                && slot.SlotName.Contains("pbs", StringComparison.Ordinal));
        PlannedBatchComponentEmission dedicatedMaterialComponent = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal));
        PlannedBatchComponentEmission albedoTexture = Assert.Single(
            batchPlan.ComponentEmissions,
            component => component.ContainerTarget.Planned == dedicatedMaterialSlot.Identity
                && string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal));

        Assert.Equal("existing-material-id", reusableMaterialReference.TargetID);
        Assert.Equal(ToPlannedTargetId(dedicatedMaterialComponent.Identity), dedicatedMaterialReference.TargetID);
        Assert.Equal(dedicatedMaterialSlot.Identity, AssertPlanned(dedicatedMaterialComponent.ContainerTarget));
        Reference albedoReference = Assert.IsType<Reference>(ToMember(dedicatedMaterialComponent.Members["AlbedoTexture"]));
        Assert.Equal(ToPlannedTargetId(albedoTexture.Identity), albedoReference.TargetID);
    }

    [Fact]
    public void CreatePlannedBatchEmission_UsesMeshAssetContainerWhenDedicatedMaterialSlotIsNotPreserved()
    {
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots = new(
            new CreatedSlot(new ResoniteSlotLocator("asset-lod-slot"), "Asset LOD"),
            new CreatedSlot(new ResoniteSlotLocator("lod-slot"), "LOD"),
            "Triangle Object",
            new PlateauResoniteLink.Targets.Resonite.ResoniteFloat3(0.0, 0.0, 0.0),
            null);
        PlannedDedicatedMaterialAsset dedicatedMaterial = new(
            new MaterialIdentity("dedicated"),
            new ResoniteMaterialBinding(
                MaterialKey: "dedicated-material",
                BaseColor: new PlateauResoniteLink.Targets.Resonite.ResoniteColor(1.0, 1.0, 1.0, 1.0),
                MaterialType: ResoniteMaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                Projection: ResoniteMaterialProjection.Uv,
                DepthOffset: null,
                SubmeshIndices: [0]),
            [
                new PlannedTextureAsset(new TextureIdentity("albedo"), new Uri("resdb:///texture/albedo")),
                new PlannedTextureAsset(new TextureIdentity("normal"), new Uri("resdb:///texture/normal")),
                new PlannedTextureAsset(new TextureIdentity("height"), new Uri("resdb:///texture/height")),
                new PlannedTextureAsset(new TextureIdentity("metallic"), new Uri("resdb:///texture/metallic")),
                new PlannedTextureAsset(new TextureIdentity("emission"), new Uri("resdb:///texture/emission")),
            ],
            PreserveDedicatedMaterialSlot: false);
        PlannedSceneObjectEmission emissionPlan = new(
            new PlannedTriangleMeshGeometryAsset(
                new GeometryIdentity("geom"),
                "Triangle Object",
                new Uri("resdb:///mesh/triangle")),
            [dedicatedMaterial],
            new PlannedRenderer(
                new GeometryIdentity("geom"),
                [new PlannedDirectRendererMaterialBinding(dedicatedMaterial.Identity)]),
            new PlannedCollider(
                new GeometryIdentity("geom"),
                true));

        PlannedBatchEmission batchPlan = Planner.Create(objectSlots, emissionPlan);

        PlannedBatchSlotEmission meshAssetSlot = Assert.Single(
            batchPlan.SlotEmissions,
            static slot => string.Equals(slot.SlotName, "Triangle Object", StringComparison.Ordinal)
                && slot.ParentTarget.Canonical == new ResoniteSlotLocator("asset-lod-slot"));
        Assert.DoesNotContain(
            batchPlan.SlotEmissions,
            slot => slot.ParentTarget.Planned == meshAssetSlot.Identity
                && !string.Equals(slot.SlotName, "Triangle Object", StringComparison.Ordinal));

        PlannedBatchComponentEmission materialComponent = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal));
        Assert.Equal(meshAssetSlot.Identity, AssertPlanned(materialComponent.ContainerTarget));
        PlannedBatchComponentEmission meshRenderer = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal));
        SyncList rendererMaterials = Assert.IsType<SyncList>(ToMember(meshRenderer.Members["Materials"]));
        Reference materialReference = Assert.IsType<Reference>(Assert.Single(rendererMaterials.Elements));
        Assert.Equal(ToPlannedTargetId(materialComponent.Identity), materialReference.TargetID);

        PlannedBatchComponentEmission[] materialTextures = batchPlan.ComponentEmissions
            .Where(component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(5, materialTextures.Length);
        Assert.All(materialTextures, texture => Assert.Equal(meshAssetSlot.Identity, AssertPlanned(texture.ContainerTarget)));

        IReadOnlyDictionary<string, PlannedBatchComponentEmission> texturesByUri = materialTextures.ToDictionary(
            static texture => Assert.IsType<PlannedLiteralMember>(texture.Members["URL"]).Value is Field_Uri uri
                ? uri.Value.ToString()
                : throw new InvalidOperationException("Texture URL member was not a URI literal."),
            static texture => texture,
            StringComparer.Ordinal);

        Assert.Equal(ToPlannedTargetId(texturesByUri["resdb:///texture/albedo"].Identity), Assert.IsType<Reference>(ToMember(materialComponent.Members["AlbedoTexture"])).TargetID);
        Assert.Equal(ToPlannedTargetId(texturesByUri["resdb:///texture/normal"].Identity), Assert.IsType<Reference>(ToMember(materialComponent.Members["NormalMap"])).TargetID);
        Assert.Equal(ToPlannedTargetId(texturesByUri["resdb:///texture/height"].Identity), Assert.IsType<Reference>(ToMember(materialComponent.Members["HeightMap"])).TargetID);
        Assert.Equal(ToPlannedTargetId(texturesByUri["resdb:///texture/metallic"].Identity), Assert.IsType<Reference>(ToMember(materialComponent.Members["MetallicMap"])).TargetID);
        Assert.Equal(ToPlannedTargetId(texturesByUri["resdb:///texture/metallic"].Identity), Assert.IsType<Reference>(ToMember(materialComponent.Members["OcclusionMap"])).TargetID);
        Assert.Equal(ToPlannedTargetId(texturesByUri["resdb:///texture/emission"].Identity), Assert.IsType<Reference>(ToMember(materialComponent.Members["EmissiveMap"])).TargetID);
        Assert.DoesNotContain("PreferredProfile", texturesByUri["resdb:///texture/albedo"].Members.Keys);
        Assert.Equal("Linear", Assert.IsType<Field_Nullable_Enum>(ToMember(texturesByUri["resdb:///texture/normal"].Members["PreferredProfile"])).Value);
        Assert.Equal("Linear", Assert.IsType<Field_Nullable_Enum>(ToMember(texturesByUri["resdb:///texture/height"].Members["PreferredProfile"])).Value);
        Assert.Equal("Linear", Assert.IsType<Field_Nullable_Enum>(ToMember(texturesByUri["resdb:///texture/metallic"].Members["PreferredProfile"])).Value);
        Assert.DoesNotContain("PreferredProfile", texturesByUri["resdb:///texture/emission"].Members.Keys);
    }

    [Fact]
    public void CreatePlannedBatchEmission_UsesMainTexturePropertyBlockForRendererOverride()
    {
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots = new(
            new CreatedSlot(new ResoniteSlotLocator("asset-lod-slot"), "Asset LOD"),
            new CreatedSlot(new ResoniteSlotLocator("lod-slot"), "LOD"),
            "Triangle Object",
            new PlateauResoniteLink.Targets.Resonite.ResoniteFloat3(0.0, 0.0, 0.0),
            null);
        PlannedReusableMaterialAsset reusableMaterial = new(new MaterialIdentity("reusable"), new ResoniteComponentLocator("shared-material-id"));
        PlannedSceneObjectEmission emissionPlan = new(
            new PlannedTriangleMeshGeometryAsset(
                new GeometryIdentity("geom"),
                "Triangle Object",
                new Uri("resdb:///mesh/triangle")),
            [reusableMaterial],
            new PlannedRenderer(
                new GeometryIdentity("geom"),
                [
                    new PlannedMainTextureOverrideRendererMaterialBinding(
                        reusableMaterial.Identity,
                        new PlannedTextureAsset(new TextureIdentity("override"), new Uri("resdb:///texture/override"))),
                ]),
            new PlannedCollider(
                new GeometryIdentity("geom"),
                false));

        PlannedBatchEmission batchPlan = Planner.Create(objectSlots, emissionPlan);

        PlannedBatchComponentEmission meshRenderer = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal));
        SyncList rendererMaterials = Assert.IsType<SyncList>(ToMember(meshRenderer.Members["Materials"]));
        SyncList rendererPropertyBlocks = Assert.IsType<SyncList>(ToMember(meshRenderer.Members["MaterialPropertyBlocks"]));
        Reference materialReference = Assert.IsType<Reference>(Assert.Single(rendererMaterials.Elements));
        Reference propertyBlockReference = Assert.IsType<Reference>(Assert.Single(rendererPropertyBlocks.Elements));
        PlannedBatchComponentEmission propertyBlockComponent = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal));
        PlannedBatchSlotEmission assetSlot = Assert.Single(
            batchPlan.SlotEmissions,
            slot => string.Equals(slot.SlotName, "Triangle Object", StringComparison.Ordinal)
                && slot.ParentTarget.Canonical == new ResoniteSlotLocator("asset-lod-slot"));
        PlannedBatchComponentEmission overrideTexture = Assert.Single(
            batchPlan.ComponentEmissions,
            component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal)
                && component.ContainerTarget.Planned == assetSlot.Identity);

        Assert.Equal("shared-material-id", materialReference.TargetID);
        Assert.Equal(ToPlannedTargetId(propertyBlockComponent.Identity), propertyBlockReference.TargetID);
        Assert.Equal(AssertPlanned(meshRenderer.ContainerTarget), AssertPlanned(propertyBlockComponent.ContainerTarget));
        Assert.Equal(ToPlannedTargetId(overrideTexture.Identity), Assert.IsType<Reference>(ToMember(propertyBlockComponent.Members["Texture"])).TargetID);
        Assert.DoesNotContain("WrapModeU", overrideTexture.Members.Keys);
        Assert.DoesNotContain("WrapModeV", overrideTexture.Members.Keys);
        Assert.DoesNotContain("PreferredProfile", overrideTexture.Members.Keys);
    }

    [Fact]
    public void CreatePlannedBatchEmission_ClampsTerrainMainTextureOverride()
    {
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots = new(
            new CreatedSlot(new ResoniteSlotLocator("asset-lod-slot"), "Asset LOD"),
            new CreatedSlot(new ResoniteSlotLocator("lod-slot"), "LOD"),
            "Triangle Object",
            new PlateauResoniteLink.Targets.Resonite.ResoniteFloat3(0.0, 0.0, 0.0),
            null);
        PlannedReusableMaterialAsset reusableMaterial = new(new MaterialIdentity("reusable"), new ResoniteComponentLocator("shared-material-id"));
        PlannedSceneObjectEmission emissionPlan = new(
            new PlannedTriangleMeshGeometryAsset(
                new GeometryIdentity("geom"),
                "Triangle Object",
                new Uri("resdb:///mesh/triangle")),
            [reusableMaterial],
            new PlannedRenderer(
                new GeometryIdentity("geom"),
                [
                    new PlannedMainTextureOverrideRendererMaterialBinding(
                        reusableMaterial.Identity,
                        new PlannedTextureAsset(new TextureIdentity("override"), new Uri("resdb:///texture/override")),
                        ClampWrapMode: true),
                ]),
            new PlannedCollider(
                new GeometryIdentity("geom"),
                false));

        PlannedBatchEmission batchPlan = Planner.Create(objectSlots, emissionPlan);

        PlannedBatchComponentEmission overrideTexture = Assert.Single(
            batchPlan.ComponentEmissions,
            component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal)
                && component.ContainerTarget.Planned == FindAssetSlotIdentity(batchPlan, "Triangle Object"));

        Assert.Equal("Clamp", Assert.IsType<Field_Enum>(ToMember(overrideTexture.Members["WrapModeU"])).Value);
        Assert.Equal("Clamp", Assert.IsType<Field_Enum>(ToMember(overrideTexture.Members["WrapModeV"])).Value);
        Assert.DoesNotContain("PreferredProfile", overrideTexture.Members.Keys);
    }

    [Fact]
    public void CreatePlannedBatchEmission_UsesDistinctOverrideComponentIdsForSharedMaterialOverrides()
    {
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots = new(
            new CreatedSlot(new ResoniteSlotLocator("asset-lod-slot"), "Asset LOD"),
            new CreatedSlot(new ResoniteSlotLocator("lod-slot"), "LOD"),
            "Triangle Object",
            new PlateauResoniteLink.Targets.Resonite.ResoniteFloat3(0.0, 0.0, 0.0),
            null);
        PlannedReusableMaterialAsset reusableMaterial = new(new MaterialIdentity("reusable"), new ResoniteComponentLocator("shared-material-id"));
        PlannedSceneObjectEmission emissionPlan = new(
            new PlannedTriangleMeshGeometryAsset(
                new GeometryIdentity("geom"),
                "Triangle Object",
                new Uri("resdb:///mesh/triangle")),
            [reusableMaterial],
            new PlannedRenderer(
                new GeometryIdentity("geom"),
                [
                    new PlannedMainTextureOverrideRendererMaterialBinding(
                        reusableMaterial.Identity,
                        new PlannedTextureAsset(new TextureIdentity("override-a"), new Uri("resdb:///texture/override-a"))),
                    new PlannedMainTextureOverrideRendererMaterialBinding(
                        reusableMaterial.Identity,
                        new PlannedTextureAsset(new TextureIdentity("override-b"), new Uri("resdb:///texture/override-b"))),
                ]),
            new PlannedCollider(
                new GeometryIdentity("geom"),
                false));

        PlannedBatchEmission batchPlan = Planner.Create(objectSlots, emissionPlan);

        PlannedBatchComponentEmission[] propertyBlocks = batchPlan.ComponentEmissions
            .Where(static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal))
            .ToArray();
        PlannedBatchComponentEmission[] textures = batchPlan.ComponentEmissions
            .Where(static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(2, propertyBlocks.Length);
        Assert.Equal(2, textures.Length);
        Assert.Equal(2, propertyBlocks.Select(static component => component.Identity.Value).Distinct().Count());
        Assert.Equal(2, textures.Select(static component => component.Identity.Value).Distinct().Count());
        Assert.Equal(
            2,
            propertyBlocks
                .Select(component => Assert.IsType<Reference>(ToMember(component.Members["Texture"])).TargetID)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    private static ResoniteSlotLocator AssertCanonical(PlannedSlotTargetReference target)
    {
        Assert.NotNull(target.Canonical);
        Assert.Null(target.Planned);
        return target.Canonical.Value;
    }

    private static BatchPlanSlotLocator AssertPlanned(PlannedSlotTargetReference target)
    {
        Assert.NotNull(target.Planned);
        Assert.Null(target.Canonical);
        return target.Planned.Value;
    }

    private static Member ToMember(PlannedMember member)
    {
        return member switch
        {
            PlannedLiteralMember literal => literal.Value,
            PlannedElementReferenceMember reference => new Reference
            {
                TargetID = ResolveTargetId(reference.Target),
            },
            PlannedSyncListMember syncList => new SyncList
            {
                Elements = syncList.Elements.Select(ToMember).ToList(),
            },
            _ => throw new InvalidOperationException($"Unsupported planned member type '{member.GetType().Name}'."),
        };
    }

    private static BatchPlanSlotLocator FindAssetSlotIdentity(PlannedBatchEmission batchPlan, string slotName)
    {
        return Assert.Single(
            batchPlan.SlotEmissions,
            slot => string.Equals(slot.SlotName, slotName, StringComparison.Ordinal)
                && slot.ParentTarget.Canonical == new ResoniteSlotLocator("asset-lod-slot")).Identity;
    }

    private static string? ResolveTargetId(PlannedWorldElementReference target)
    {
        if (target.CanonicalSlot is ResoniteSlotLocator canonicalSlot)
        {
            return canonicalSlot.Value;
        }

        if (target.CanonicalComponent is ResoniteComponentLocator canonicalComponent)
        {
            return canonicalComponent.Value;
        }

        if (target.PlannedSlot is BatchPlanSlotLocator plannedSlot)
        {
            return ToPlannedTargetId(plannedSlot);
        }

        return target.PlannedComponent is BatchPlanComponentLocator plannedComponent
            ? ToPlannedTargetId(plannedComponent)
            : null;
    }

    private static string ToPlannedTargetId(BatchPlanSlotLocator locator)
    {
        return locator.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static string ToPlannedTargetId(BatchPlanComponentLocator locator)
    {
        return locator.Value.ToString(CultureInfo.InvariantCulture);
    }
}
