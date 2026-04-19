using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Targets;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResoniteSceneBatchEmissionPlanningTests
{
    private static readonly IResoniteBatchEmissionPlanner Planner = new ResoniteBatchEmissionPlanner();

    [Fact]
    public void CreatePlannedBatchEmission_CreatesHeightMapGridPlanWithPlannedTextureReference()
    {
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots = new(
            new CreatedSlot("asset-lod-slot", "Asset LOD"),
            new CreatedSlot("lod-slot", "LOD"),
            "HeightMap Object",
            new Plateau.ResoniteLink.Domain.Importing.ResoniteFloat3(1.0, 2.0, 3.0),
            null);
        PlannedSceneObjectEmission emissionPlan = new(
            new PlannedHeightMapGridGeometryAsset(
                new GeometryIdentity("geom"),
                "HeightMap Object",
                "HeightMap Object_heightmap",
                new Plateau.ResoniteLink.Domain.Importing.ResoniteHeightMapGridGeometry(
                    Width: 2,
                    Height: 3,
                    Size: new Plateau.ResoniteLink.Domain.Importing.ResoniteFloat2(10.0, 20.0),
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
                && string.Equals(slot.ParentId, "asset-lod-slot", StringComparison.Ordinal));
        PlannedBatchSlotEmission heightMapSlot = Assert.Single(
            batchPlan.SlotEmissions,
            static slot => string.Equals(slot.SlotName, "HeightMap Object_heightmap", StringComparison.Ordinal));
        PlannedBatchComponentEmission heightTexture = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal));
        PlannedBatchComponentEmission gridMesh = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.GridMesh", StringComparison.Ordinal));

        Assert.Equal("asset-lod-slot", meshAssetSlot.ParentId);
        Assert.Equal("asset-lod-slot", heightMapSlot.ParentId);
        Assert.Equal(heightMapSlot.Identity.Value, heightTexture.ContainerId);
        Reference displacementTexture = Assert.IsType<Reference>(gridMesh.Members["DisplacementTexture"]);
        Assert.Equal(heightTexture.Identity.Value, displacementTexture.TargetID);
    }

    [Fact]
    public void CreatePlannedBatchEmission_UsesReusableTargetsAndPlansDedicatedMaterialComponents()
    {
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots = new(
            new CreatedSlot("asset-lod-slot", "Asset LOD"),
            new CreatedSlot("lod-slot", "LOD"),
            "Triangle Object",
            new Plateau.ResoniteLink.Domain.Importing.ResoniteFloat3(0.0, 0.0, 0.0),
            null);
        PlannedDedicatedMaterialAsset dedicatedMaterial = new(
            new MaterialIdentity("dedicated"),
            new Plateau.ResoniteLink.Domain.Importing.ResoniteMaterialBinding(
                MaterialKey: "dedicated-material",
                BaseColor: new Plateau.ResoniteLink.Domain.Importing.ResoniteColor(1.0, 1.0, 1.0, 1.0),
                MaterialType: Plateau.ResoniteLink.Domain.Importing.ResoniteMaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind: Plateau.ResoniteLink.Domain.Importing.ResoniteTextureSourceKind.Dataset,
                Projection: Plateau.ResoniteLink.Domain.Importing.ResoniteMaterialProjection.Uv,
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
                new PlannedReusableMaterialAsset(new MaterialIdentity("reusable"), "existing-material-id"),
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
        SyncList materials = Assert.IsType<SyncList>(meshRenderer.Members["Materials"]);
        Reference reusableMaterialReference = Assert.IsType<Reference>(materials.Elements[0]);
        Reference dedicatedMaterialReference = Assert.IsType<Reference>(materials.Elements[1]);
        PlannedBatchSlotEmission dedicatedMaterialSlot = Assert.Single(
            batchPlan.SlotEmissions,
            slot => string.Equals(
                slot.SlotName,
                ResoniteSceneMaterialConventions.CreateMaterialSlotName(dedicatedMaterial.Material, useCommonMaterialAssets: false),
                StringComparison.Ordinal));
        PlannedBatchComponentEmission dedicatedMaterialComponent = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal));
        PlannedBatchComponentEmission albedoTexture = Assert.Single(
            batchPlan.ComponentEmissions,
            component => string.Equals(component.ContainerId, dedicatedMaterialSlot.Identity.Value, StringComparison.Ordinal)
                && string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal));

        Assert.Equal("existing-material-id", reusableMaterialReference.TargetID);
        Assert.Equal(dedicatedMaterialComponent.Identity.Value, dedicatedMaterialReference.TargetID);
        Assert.Equal(dedicatedMaterialSlot.Identity.Value, dedicatedMaterialComponent.ContainerId);
        Reference albedoReference = Assert.IsType<Reference>(dedicatedMaterialComponent.Members["AlbedoTexture"]);
        Assert.Equal(albedoTexture.Identity.Value, albedoReference.TargetID);
    }

    [Fact]
    public void CreatePlannedBatchEmission_UsesMeshAssetContainerWhenDedicatedMaterialSlotIsNotPreserved()
    {
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots = new(
            new CreatedSlot("asset-lod-slot", "Asset LOD"),
            new CreatedSlot("lod-slot", "LOD"),
            "Triangle Object",
            new Plateau.ResoniteLink.Domain.Importing.ResoniteFloat3(0.0, 0.0, 0.0),
            null);
        PlannedDedicatedMaterialAsset dedicatedMaterial = new(
            new MaterialIdentity("dedicated"),
            new Plateau.ResoniteLink.Domain.Importing.ResoniteMaterialBinding(
                MaterialKey: "dedicated-material",
                BaseColor: new Plateau.ResoniteLink.Domain.Importing.ResoniteColor(1.0, 1.0, 1.0, 1.0),
                MaterialType: Plateau.ResoniteLink.Domain.Importing.ResoniteMaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind: Plateau.ResoniteLink.Domain.Importing.ResoniteTextureSourceKind.Bundled,
                Projection: Plateau.ResoniteLink.Domain.Importing.ResoniteMaterialProjection.Uv,
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
                && string.Equals(slot.ParentId, "asset-lod-slot", StringComparison.Ordinal));
        Assert.DoesNotContain(
            batchPlan.SlotEmissions,
            slot => string.Equals(slot.ParentId, meshAssetSlot.Identity.Value, StringComparison.Ordinal)
                && !string.Equals(slot.SlotName, "Triangle Object", StringComparison.Ordinal));

        PlannedBatchComponentEmission materialComponent = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal));
        Assert.Equal(meshAssetSlot.Identity.Value, materialComponent.ContainerId);
        PlannedBatchComponentEmission meshRenderer = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal));
        SyncList rendererMaterials = Assert.IsType<SyncList>(meshRenderer.Members["Materials"]);
        Reference materialReference = Assert.IsType<Reference>(Assert.Single(rendererMaterials.Elements));
        Assert.Equal(materialComponent.Identity.Value, materialReference.TargetID);

        PlannedBatchComponentEmission[] materialTextures = batchPlan.ComponentEmissions
            .Where(component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(5, materialTextures.Length);
        Assert.All(materialTextures, texture => Assert.Equal(meshAssetSlot.Identity.Value, texture.ContainerId));

        IReadOnlyDictionary<string, PlannedBatchComponentEmission> texturesByUri = materialTextures.ToDictionary(
            static texture => ((Field_Uri)texture.Members["URL"]).Value.ToString(),
            static texture => texture,
            StringComparer.Ordinal);

        Assert.Equal(texturesByUri["resdb:///texture/albedo"].Identity.Value, Assert.IsType<Reference>(materialComponent.Members["AlbedoTexture"]).TargetID);
        Assert.Equal(texturesByUri["resdb:///texture/normal"].Identity.Value, Assert.IsType<Reference>(materialComponent.Members["NormalMap"]).TargetID);
        Assert.Equal(texturesByUri["resdb:///texture/height"].Identity.Value, Assert.IsType<Reference>(materialComponent.Members["HeightMap"]).TargetID);
        Assert.Equal(texturesByUri["resdb:///texture/metallic"].Identity.Value, Assert.IsType<Reference>(materialComponent.Members["MetallicMap"]).TargetID);
        Assert.Equal(texturesByUri["resdb:///texture/metallic"].Identity.Value, Assert.IsType<Reference>(materialComponent.Members["OcclusionMap"]).TargetID);
        Assert.Equal(texturesByUri["resdb:///texture/emission"].Identity.Value, Assert.IsType<Reference>(materialComponent.Members["EmissiveMap"]).TargetID);
    }

    [Fact]
    public void CreatePlannedBatchEmission_UsesMainTexturePropertyBlockForRendererOverride()
    {
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots = new(
            new CreatedSlot("asset-lod-slot", "Asset LOD"),
            new CreatedSlot("lod-slot", "LOD"),
            "Triangle Object",
            new Plateau.ResoniteLink.Domain.Importing.ResoniteFloat3(0.0, 0.0, 0.0),
            null);
        PlannedReusableMaterialAsset reusableMaterial = new(new MaterialIdentity("reusable"), "shared-material-id");
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
        SyncList rendererMaterials = Assert.IsType<SyncList>(meshRenderer.Members["Materials"]);
        SyncList rendererPropertyBlocks = Assert.IsType<SyncList>(meshRenderer.Members["MaterialPropertyBlocks"]);
        Reference materialReference = Assert.IsType<Reference>(Assert.Single(rendererMaterials.Elements));
        Reference propertyBlockReference = Assert.IsType<Reference>(Assert.Single(rendererPropertyBlocks.Elements));
        PlannedBatchComponentEmission propertyBlockComponent = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal));
        PlannedBatchSlotEmission assetSlot = Assert.Single(
            batchPlan.SlotEmissions,
            static slot => string.Equals(slot.Identity.Value, "plan:mesh-asset-slot", StringComparison.Ordinal));
        PlannedBatchComponentEmission overrideTexture = Assert.Single(
            batchPlan.ComponentEmissions,
            component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal)
                && string.Equals(component.ContainerId, assetSlot.Identity.Value, StringComparison.Ordinal));

        Assert.Equal("shared-material-id", materialReference.TargetID);
        Assert.Equal(propertyBlockComponent.Identity.Value, propertyBlockReference.TargetID);
        Assert.Equal(propertyBlockComponent.ContainerId, meshRenderer.ContainerId);
        Assert.Equal(overrideTexture.Identity.Value, Assert.IsType<Reference>(propertyBlockComponent.Members["Texture"]).TargetID);
    }

    [Fact]
    public void CreatePlannedBatchEmission_UsesDistinctOverrideComponentIdsForSharedMaterialOverrides()
    {
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots = new(
            new CreatedSlot("asset-lod-slot", "Asset LOD"),
            new CreatedSlot("lod-slot", "LOD"),
            "Triangle Object",
            new Plateau.ResoniteLink.Domain.Importing.ResoniteFloat3(0.0, 0.0, 0.0),
            null);
        PlannedReusableMaterialAsset reusableMaterial = new(new MaterialIdentity("reusable"), "shared-material-id");
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
                .Select(component => Assert.IsType<Reference>(component.Members["Texture"]).TargetID)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }
}
