using System;
using System.Collections.Generic;
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
    public void CreatePlannedBatchEmission_CreatesHeightMapGridPlanWithPlannedTextureReference()
    {
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots = new(
            new CreatedSlot(new ResoniteSlotLocator("asset-lod-slot"), "Asset LOD"),
            new CreatedSlot(new ResoniteSlotLocator("lod-slot"), "LOD"),
            "HeightMap Object",
            new PlateauResoniteLink.Targets.Resonite.ResoniteFloat3(1.0, 2.0, 3.0),
            null);
        PlannedSceneObjectEmission emissionPlan = new(
            new PlannedHeightMapGridGeometryAsset(
                new GeometryIdentity("geom"),
                "HeightMap Object",
                "HeightMap Object_heightmap",
                new ResoniteHeightMapGridGeometry(
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
            static slot => string.Equals(slot.SlotName, "HeightMap Object", StringComparison.Ordinal)
                && slot.ParentTarget.Canonical == new ResoniteSlotLocator("asset-lod-slot"));
        PlannedBatchSlotEmission heightMapSlot = Assert.Single(
            batchPlan.SlotEmissions,
            static slot => string.Equals(slot.SlotName, "HeightMap Object_heightmap", StringComparison.Ordinal));
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
        Assert.Equal(heightTexture.Identity.Value, displacementTexture.TargetID);
    }

    [Fact]
    public void CreatePlannedBatchEmission_CarriesHeightMapGridUvMembers()
    {
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots = new(
            new CreatedSlot(new ResoniteSlotLocator("asset-lod-slot"), "Asset LOD"),
            new CreatedSlot(new ResoniteSlotLocator("lod-slot"), "LOD"),
            "HeightMap Object",
            new PlateauResoniteLink.Targets.Resonite.ResoniteFloat3(1.0, 2.0, 3.0),
            null);
        PlannedSceneObjectEmission emissionPlan = new(
            new PlannedHeightMapGridGeometryAsset(
                new GeometryIdentity("geom"),
                "HeightMap Object",
                "HeightMap Object_heightmap",
                new ResoniteHeightMapGridGeometry(
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
            slot => slot.ParentTarget.Planned == new BatchPlanSlotLocator("plan:mesh-asset-slot")
                && slot.SlotName.Contains("pbs", StringComparison.Ordinal));
        PlannedBatchComponentEmission dedicatedMaterialComponent = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal));
        PlannedBatchComponentEmission albedoTexture = Assert.Single(
            batchPlan.ComponentEmissions,
            component => component.ContainerTarget.Planned == dedicatedMaterialSlot.Identity
                && string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal));

        Assert.Equal("existing-material-id", reusableMaterialReference.TargetID);
        Assert.Equal(dedicatedMaterialComponent.Identity.Value, dedicatedMaterialReference.TargetID);
        Assert.Equal(dedicatedMaterialSlot.Identity, AssertPlanned(dedicatedMaterialComponent.ContainerTarget));
        Reference albedoReference = Assert.IsType<Reference>(ToMember(dedicatedMaterialComponent.Members["AlbedoTexture"]));
        Assert.Equal(albedoTexture.Identity.Value, albedoReference.TargetID);
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
        Assert.Equal(materialComponent.Identity.Value, materialReference.TargetID);

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

        Assert.Equal(texturesByUri["resdb:///texture/albedo"].Identity.Value, Assert.IsType<Reference>(ToMember(materialComponent.Members["AlbedoTexture"])).TargetID);
        Assert.Equal(texturesByUri["resdb:///texture/normal"].Identity.Value, Assert.IsType<Reference>(ToMember(materialComponent.Members["NormalMap"])).TargetID);
        Assert.Equal(texturesByUri["resdb:///texture/height"].Identity.Value, Assert.IsType<Reference>(ToMember(materialComponent.Members["HeightMap"])).TargetID);
        Assert.Equal(texturesByUri["resdb:///texture/metallic"].Identity.Value, Assert.IsType<Reference>(ToMember(materialComponent.Members["MetallicMap"])).TargetID);
        Assert.Equal(texturesByUri["resdb:///texture/metallic"].Identity.Value, Assert.IsType<Reference>(ToMember(materialComponent.Members["OcclusionMap"])).TargetID);
        Assert.Equal(texturesByUri["resdb:///texture/emission"].Identity.Value, Assert.IsType<Reference>(ToMember(materialComponent.Members["EmissiveMap"])).TargetID);
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
        Assert.Equal(propertyBlockComponent.Identity.Value, propertyBlockReference.TargetID);
        Assert.Equal(AssertPlanned(meshRenderer.ContainerTarget), AssertPlanned(propertyBlockComponent.ContainerTarget));
        Assert.Equal(overrideTexture.Identity.Value, Assert.IsType<Reference>(ToMember(propertyBlockComponent.Members["Texture"])).TargetID);
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
                && component.ContainerTarget.Planned == new BatchPlanSlotLocator("plan:mesh-asset-slot"));

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
        Assert.Equal(2, propertyBlocks.Select(static component => component.Identity.Value).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, textures.Select(static component => component.Identity.Value).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            2,
            propertyBlocks
                .Select(component => Assert.IsType<Reference>(ToMember(component.Members["Texture"])).TargetID)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    [Fact]
    public void CreatePlannedBatchEmission_AddsVisualFallbackMeshRendererWithoutFallbackCollider()
    {
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots = new(
            new CreatedSlot(new ResoniteSlotLocator("asset-lod-slot"), "Asset LOD"),
            new CreatedSlot(new ResoniteSlotLocator("lod-slot"), "LOD"),
            "HeightMap Object",
            new PlateauResoniteLink.Domain.Importing.ResoniteFloat3(1.0, 2.0, 3.0),
            null);
        PlannedReusableMaterialAsset reusableMaterial = new(
            new MaterialIdentity("reusable"),
            new ResoniteComponentLocator("shared-material-id"));
        PlannedSceneObjectEmission emissionPlan = new(
            new PlannedHeightMapGridGeometryAsset(
                new GeometryIdentity("geom"),
                "HeightMap Object",
                "HeightMap Object_heightmap",
                new ResoniteHeightMapGridGeometry(
                    Width: 2,
                    Height: 2,
                    Size: new PlateauResoniteLink.Domain.Importing.ResoniteFloat2(10.0, 20.0),
                    MinHeight: 0.0,
                    MaxHeight: 6.0,
                    HeightSamples: [0.0, 1.0, 2.0, 3.0]),
                new Uri("resdb:///texture/height")),
            [reusableMaterial],
            new PlannedRenderer(
                new GeometryIdentity("geom"),
                [new PlannedDirectRendererMaterialBinding(reusableMaterial.Identity)]),
            new PlannedCollider(
                new GeometryIdentity("geom"),
                true),
            [
                new PlannedVisualFallbackEmission(
                    new PlannedTriangleMeshGeometryAsset(
                        new GeometryIdentity("geom:fallback"),
                        "HeightMap Object_heightmap_skirt",
                        new Uri("resdb:///mesh/skirt")),
                    new PlannedRenderer(
                        new GeometryIdentity("geom:fallback"),
                        [new PlannedDirectRendererMaterialBinding(reusableMaterial.Identity)])),
            ]);

        PlannedBatchEmission batchPlan = Planner.Create(objectSlots, emissionPlan);

        PlannedBatchComponentEmission[] meshRenderers = batchPlan.ComponentEmissions
            .Where(static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal))
            .ToArray();
        PlannedBatchComponentEmission[] meshColliders = batchPlan.ComponentEmissions
            .Where(static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MeshCollider", StringComparison.Ordinal))
            .ToArray();
        PlannedBatchComponentEmission[] staticMeshes = batchPlan.ComponentEmissions
            .Where(static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticMesh", StringComparison.Ordinal))
            .ToArray();
        PlannedBatchSlotEmission fallbackAssetSlot = Assert.Single(
            batchPlan.SlotEmissions,
            static slot => string.Equals(slot.SlotName, "HeightMap Object_heightmap_skirt", StringComparison.Ordinal));

        Assert.Equal(2, meshRenderers.Length);
        Assert.Single(meshColliders);
        PlannedBatchComponentEmission fallbackStaticMesh = Assert.Single(staticMeshes);
        Assert.Equal(
            "resdb:///mesh/skirt",
            Assert.IsType<Field_Uri>(ToMember(fallbackStaticMesh.Members["URL"])).Value.ToString());
        Assert.Equal(fallbackAssetSlot.Identity, AssertPlanned(fallbackStaticMesh.ContainerTarget));
        Assert.All(
            meshRenderers,
            renderer =>
            {
                SyncList materials = Assert.IsType<SyncList>(ToMember(renderer.Members["Materials"]));
                Assert.Equal("shared-material-id", Assert.IsType<Reference>(Assert.Single(materials.Elements)).TargetID);
            });
        Assert.DoesNotContain(
            meshColliders,
            collider => Assert.IsType<Reference>(ToMember(collider.Members["Mesh"])).TargetID == fallbackStaticMesh.Identity.Value);
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
                TargetID = reference.Target.CanonicalSlot?.Value
                    ?? reference.Target.CanonicalComponent?.Value
                    ?? reference.Target.PlannedSlot?.Value
                    ?? reference.Target.PlannedComponent?.Value,
            },
            PlannedSyncListMember syncList => new SyncList
            {
                Elements = syncList.Elements.Select(ToMember).ToList(),
            },
            _ => throw new InvalidOperationException($"Unsupported planned member type '{member.GetType().Name}'."),
        };
    }
}
