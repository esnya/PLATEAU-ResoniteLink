using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Targets;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResoniteSceneBatchEmissionPlanningTests
{
    [Fact]
    public void CreatePlannedBatchEmission_CreatesHeightMapGridPlanWithPlannedTextureReference()
    {
        ResoniteLinkSceneBuilder.ObjectSlotHierarchy objectSlots = new(
            new CreatedSlot("asset-lod-slot", "Asset LOD"),
            new CreatedSlot("lod-slot", "LOD"),
            "HeightMap Object",
            "heightmap-tag",
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

        PlannedBatchEmission batchPlan = ResoniteLinkSceneBuilder.CreatePlannedBatchEmission(objectSlots, emissionPlan);

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
        ResoniteLinkSceneBuilder.ObjectSlotHierarchy objectSlots = new(
            new CreatedSlot("asset-lod-slot", "Asset LOD"),
            new CreatedSlot("lod-slot", "LOD"),
            "Triangle Object",
            "triangle-tag",
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
                [new MaterialIdentity("reusable"), dedicatedMaterial.Identity]),
            new PlannedCollider(
                new GeometryIdentity("geom"),
                false));

        PlannedBatchEmission batchPlan = ResoniteLinkSceneBuilder.CreatePlannedBatchEmission(objectSlots, emissionPlan);

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
                ResoniteLinkSceneBuilder.CreateMaterialSlotName(dedicatedMaterial.Material, useCommonMaterialAssets: false),
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
}
