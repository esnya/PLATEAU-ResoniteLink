using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Execution;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResoniteSceneBatchEmissionPlanningTests
{
    private static PlannedBatchEmission CreateBatchPlan(
        ResoniteObjectSlotHierarchy objectSlots,
        PlannedGeometryAsset geometryAsset,
        IReadOnlyList<PlannedMaterialAsset> materialAssets,
        IReadOnlyList<PlannedRendererMaterialBinding> rendererMaterialBindings,
        bool collisionEnabled)
    {
        return ResoniteBatchEmissionPlanner.Create(
            objectSlots,
            geometryAsset,
            materialAssets,
            rendererMaterialBindings,
            collisionEnabled);
    }

    [Fact]
    public void PlannedDriverTargetBundle_CreatePairsFieldTargetAndDefaultValue()
    {
        PlannedDriverTargetBundle bundle = PlannedDriverTargetBundle.Create(
            new Field_bool
            {
                Value = false,
            });

        Assert.Same(bundle.Field.Field, AssertPlannedField(bundle.Target.Target));
        Assert.False(Assert.IsType<Field_bool>(bundle.Field.Value).Value);
        Assert.False(Assert.IsType<Field_bool>(bundle.DefaultValue.Value).Value);
        Assert.NotSame(bundle.Field.Value, bundle.DefaultValue.Value);
    }

    [Fact]
    public void PlannedBatchEmissionCreateRejectsForwardPlannedSlotParentReference()
    {
        PlannedBatchSlotEmission? parent = null;
        PlannedBatchSlotEmission child = new(
            PlannedSlotTargetReference.PlannedSlot(parent = new PlannedBatchSlotEmission(
                PlannedSlotTargetReference.CanonicalSlot(new ResoniteSlotLocator("root")),
                "parent",
                null,
                null)),
            "child",
            null,
            null);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => PlannedBatchEmission.Create(
                [
                    child,
                    parent,
                ],
                [],
                [child, parent],
                []));

        Assert.Equal("Planned slot parent target must reference an earlier planned slot.", error.Message);
    }

    [Fact]
    public void PlannedBatchEmissionCreateRejectsForwardPlannedComponentMemberReference()
    {
        PlannedBatchSlotEmission container = new(
            PlannedSlotTargetReference.CanonicalSlot(new ResoniteSlotLocator("root")),
            "container",
            null,
            null);
        PlannedBatchComponentEmission? target = null;
        PlannedBatchComponentEmission source = new(
            PlannedSlotTargetReference.PlannedSlot(container),
            "[FrooxEngine]FrooxEngine.Source",
            new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
            {
                ["Target"] = PlannedMembers.Reference(PlannedWorldElementReference.Planned(target = new PlannedBatchComponentEmission(
                    PlannedSlotTargetReference.PlannedSlot(container),
                    "[FrooxEngine]FrooxEngine.Target",
                    new Dictionary<string, PlannedMember>(StringComparer.Ordinal)))),
            });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => PlannedBatchEmission.Create(
                [
                    container,
                ],
                [
                    source,
                    target,
                ],
                [container],
                [source, target]));

        Assert.Equal("Planned world element target must reference an earlier planned component.", error.Message);
    }

    [Fact]
    public void CreatePlannedBatchEmission_CreatesTerrainGridPlanWithPlannedTextureReference()
    {
        ResoniteObjectSlotHierarchy objectSlots = new(
            new CreatedSlot(new ResoniteSlotLocator("asset-lod-slot"), "Asset LOD"),
            new CreatedSlot(new ResoniteSlotLocator("lod-slot"), "LOD"),
            "Terrain Grid Object",
            new PlateauResoniteLink.Targets.Resonite.ResoniteFloat3(1.0, 2.0, 3.0),
            null);
        PlannedBatchEmission batchPlan = CreateBatchPlan(
            objectSlots,
            new PlannedTerrainGridGeometryAsset(
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
            [],
            true);

        PlannedBatchComponentEmission heightTexture = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal));
        PlannedBatchComponentEmission gridMesh = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.GridMesh", StringComparison.Ordinal));
        PlannedBatchComponentEmission pointsGradientDriver = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => component.ComponentType.Contains("ValueGradientDriver", StringComparison.Ordinal));
        PlannedBatchComponentEmission pointsProgressDriver = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => component.ComponentType.Contains("DynamicValueVariableDriver", StringComparison.Ordinal));
        PlannedBatchComponentEmission meshRenderer = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal));
        PlannedBatchComponentEmission meshCollider = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MeshCollider", StringComparison.Ordinal));

        Assert.Equal("Clamp", Assert.IsType<Field_Enum>(ToMember(heightTexture.Members["WrapModeU"])).Value);
        Assert.Equal("Clamp", Assert.IsType<Field_Enum>(ToMember(heightTexture.Members["WrapModeV"])).Value);
        Assert.Equal("Point", Assert.IsType<Field_Nullable_Enum>(ToMember(heightTexture.Members["FilterMode"])).Value);
        Assert.False(Assert.IsType<Field_bool>(ToMember(heightTexture.Members["MipMaps"])).Value);
        Reference displacementTexture = Assert.IsType<Reference>(ToMember(gridMesh.Members["DisplacementTexture"]));
        Assert.Equal(ToPlannedTargetId(heightTexture), displacementTexture.TargetID);
        PlannedAddressableFieldMember gridPointsMember = Assert.IsType<PlannedAddressableFieldMember.Int2>(gridMesh.Members["Points"]);
        Field_int2 gridPoints = Assert.IsType<Field_int2>(gridPointsMember.Value);
        Assert.Equal(2, gridPoints.Value.x);
        Assert.Equal(3, gridPoints.Value.y);
        Assert.True(string.IsNullOrWhiteSpace(gridPoints.ID));
        PlannedAddressableFieldMember progress = Assert.IsType<PlannedAddressableFieldMember.Float>(pointsGradientDriver.Members["Progress"]);
        Assert.Equal(1.0f, Assert.IsType<Field_float>(progress.Value).Value);
        PlannedElementReferenceMember pointsTarget = Assert.IsType<PlannedElementReferenceMember>(pointsGradientDriver.Members["Target"]);
        Assert.Same(gridPointsMember.Field, AssertPlannedField(pointsTarget.Target));
        SyncList gradientPoints = Assert.IsType<SyncList>(ToMember(pointsGradientDriver.Members["Points"]));
        Assert.Equal(2, gradientPoints.Elements.Count);
        AssertGradientPoint(gradientPoints.Elements[0], 0.0f, 2, 2);
        AssertGradientPoint(gradientPoints.Elements[1], 1.0f, 2, 3);
        Assert.Equal("PLATEAU.Terrain.Grid.Detail", Assert.IsType<Field_string>(ToMember(pointsProgressDriver.Members["VariableName"])).Value);
        PlannedElementReferenceMember progressTarget = Assert.IsType<PlannedElementReferenceMember>(pointsProgressDriver.Members["Target"]);
        Assert.Same(progress.Field, AssertPlannedField(progressTarget.Target));
        Assert.Equal(1.0f, Assert.IsType<Field_float>(ToMember(pointsProgressDriver.Members["DefaultValue"])).Value);
        Assert.Equal(ToPlannedTargetId(gridMesh), Assert.IsType<Reference>(ToMember(meshRenderer.Members["Mesh"])).TargetID);
        Assert.Equal(ToPlannedTargetId(gridMesh), Assert.IsType<Reference>(ToMember(meshCollider.Members["Mesh"])).TargetID);
    }

    [Fact]
    public void CreatePlannedBatchEmission_CreatesDynamicTerrainPlanWithGridAsFalseFallback()
    {
        ResoniteObjectSlotHierarchy objectSlots = new(
            new CreatedSlot(new ResoniteSlotLocator("asset-lod-slot"), "Asset LOD"),
            new CreatedSlot(new ResoniteSlotLocator("lod-slot"), "LOD"),
            "Dynamic Terrain Object",
            new PlateauResoniteLink.Targets.Resonite.ResoniteFloat3(1.0, 2.0, 3.0),
            null);
        PlannedBatchEmission batchPlan = CreateBatchPlan(
            objectSlots,
            new PlannedDynamicTerrainGeometryAsset(
                "Dynamic Terrain Object",
                new Uri("resdb:///mesh/static"),
                "Dynamic Terrain Object_terrain-grid",
                new ResoniteTerrainGridGeometry(
                    Width: 4,
                    Height: 5,
                    Size: new PlateauResoniteLink.Targets.Resonite.ResoniteFloat2(40.0, 50.0),
                    MinHeight: 1.0,
                    MaxHeight: 11.0,
                    HeightSamples: Enumerable.Range(0, 20).Select(static value => (double)value).ToArray()),
                new Uri("resdb:///texture/height")),
            [],
            [],
            true);

        PlannedBatchComponentEmission staticMesh = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticMesh", StringComparison.Ordinal));
        PlannedBatchComponentEmission gridMesh = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.GridMesh", StringComparison.Ordinal));
        PlannedBatchComponentEmission meshRenderer = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal));
        PlannedBatchComponentEmission meshCollider = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MeshCollider", StringComparison.Ordinal));
        PlannedBatchComponentEmission[] meshSwitches = batchPlan.ComponentEmissions
            .Where(static component => component.ComponentType.Contains("BooleanAssetDriver", StringComparison.Ordinal))
            .ToArray();
        PlannedBatchComponentEmission[] boolDrivers = batchPlan.ComponentEmissions
            .Where(static component => component.ComponentType.Contains("DynamicValueVariableDriver", StringComparison.Ordinal)
                && component.ComponentType.Contains("bool", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(2, meshSwitches.Length);
        Assert.Equal(2, boolDrivers.Length);
        PlannedAddressableReferenceMember rendererMesh = Assert.IsType<PlannedAddressableReferenceMember>(meshRenderer.Members["Mesh"]);
        PlannedAddressableReferenceMember colliderMesh = Assert.IsType<PlannedAddressableReferenceMember>(meshCollider.Members["Mesh"]);
        Assert.Equal(ToPlannedTargetId(gridMesh), Assert.IsType<Reference>(ToMember(rendererMesh)).TargetID);
        Assert.Equal(ToPlannedTargetId(gridMesh), Assert.IsType<Reference>(ToMember(colliderMesh)).TargetID);
        foreach (PlannedBatchComponentEmission meshSwitch in meshSwitches)
        {
            PlannedAddressableFieldMember.Bool state = Assert.IsType<PlannedAddressableFieldMember.Bool>(meshSwitch.Members["State"]);
            PlannedElementReferenceMember target = Assert.IsType<PlannedElementReferenceMember>(meshSwitch.Members["Target"]);
            Assert.True(
                AssertPlannedField(target.Target) is PlannedFieldReference plannedTarget
                && new[] { rendererMesh.Field, colliderMesh.Field }.Contains(plannedTarget));
            Assert.False(Assert.IsType<Field_bool>(state.Value).Value);
            Assert.Equal(ToPlannedTargetId(gridMesh), Assert.IsType<Reference>(ToMember(meshSwitch.Members["FalseTarget"])).TargetID);
            Assert.Equal(ToPlannedTargetId(staticMesh), Assert.IsType<Reference>(ToMember(meshSwitch.Members["TrueTarget"])).TargetID);
        }

        foreach (PlannedBatchComponentEmission boolDriver in boolDrivers)
        {
            PlannedElementReferenceMember target = Assert.IsType<PlannedElementReferenceMember>(boolDriver.Members["Target"]);
            Assert.True(
                AssertPlannedField(target.Target) is PlannedFieldReference plannedStateTarget
                && meshSwitches
                    .Select(meshSwitch => Assert.IsType<PlannedAddressableFieldMember.Bool>(meshSwitch.Members["State"]).Field)
                    .Contains(plannedStateTarget));
            Assert.Equal("PLATEAU.Terrain.Static.Enabled", Assert.IsType<Field_string>(ToMember(boolDriver.Members["VariableName"])).Value);
            Assert.False(Assert.IsType<Field_bool>(ToMember(boolDriver.Members["DefaultValue"])).Value);
        }
    }

    [Fact]
    public void CreatePlannedBatchEmission_CarriesTerrainGridUvMembers()
    {
        ResoniteObjectSlotHierarchy objectSlots = new(
            new CreatedSlot(new ResoniteSlotLocator("asset-lod-slot"), "Asset LOD"),
            new CreatedSlot(new ResoniteSlotLocator("lod-slot"), "LOD"),
            "Terrain Grid Object",
            new PlateauResoniteLink.Targets.Resonite.ResoniteFloat3(1.0, 2.0, 3.0),
            null);
        PlannedBatchEmission batchPlan = CreateBatchPlan(
            objectSlots,
            new PlannedTerrainGridGeometryAsset(
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
            [],
            true);

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
        ResoniteObjectSlotHierarchy objectSlots = new(
            new CreatedSlot(new ResoniteSlotLocator("asset-lod-slot"), "Asset LOD"),
            new CreatedSlot(new ResoniteSlotLocator("lod-slot"), "LOD"),
            "Triangle Object",
            new PlateauResoniteLink.Targets.Resonite.ResoniteFloat3(0.0, 0.0, 0.0),
            null);
        PlannedDedicatedMaterialAsset dedicatedMaterial = new(
            new ResoniteMaterialBinding(
                BaseColor: new PlateauResoniteLink.Targets.Resonite.ResoniteColor(1.0, 1.0, 1.0, 1.0),
                MaterialType: ResoniteMaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                Projection: ResoniteMaterialProjection.Uv,
                DepthOffset: null,
                SubmeshIndices: [0],
                ResoniteMaterialAssetBinding.Presentation),
            [new PlannedTextureAsset(ResoniteSceneMaterialConventions.PlannedTextureRole.Albedo, new Uri("resdb:///texture/albedo"))],
            PreserveDedicatedMaterialSlot: true,
            DedicatedMaterialSlotName: "material-000-pbs-uv-uv");
        PlannedReusableMaterialAsset reusableMaterial = new(new ResoniteComponentLocator("existing-material-id"));
        PlannedBatchEmission batchPlan = CreateBatchPlan(
            objectSlots,
            new PlannedTriangleMeshGeometryAsset(
                "Triangle Object",
                new Uri("resdb:///mesh/triangle")),
            [
                reusableMaterial,
                dedicatedMaterial,
            ],
            [
                new PlannedDirectRendererMaterialBinding(reusableMaterial),
                new PlannedDirectRendererMaterialBinding(dedicatedMaterial),
            ],
            false);

        PlannedBatchComponentEmission meshRenderer = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal));
        SyncList materials = Assert.IsType<SyncList>(ToMember(meshRenderer.Members["Materials"]));
        Reference reusableMaterialReference = Assert.IsType<Reference>(materials.Elements[0]);
        Reference dedicatedMaterialReference = Assert.IsType<Reference>(materials.Elements[1]);
        PlannedBatchComponentEmission dedicatedMaterialComponent = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal));
        PlannedBatchComponentEmission albedoTexture = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal));

        Assert.Equal("existing-material-id", reusableMaterialReference.TargetID);
        Assert.Equal(ToPlannedTargetId(dedicatedMaterialComponent), dedicatedMaterialReference.TargetID);
        Reference albedoReference = Assert.IsType<Reference>(ToMember(dedicatedMaterialComponent.Members["AlbedoTexture"]));
        Assert.Equal(ToPlannedTargetId(albedoTexture), albedoReference.TargetID);
    }

    [Fact]
    public void CreatePlannedBatchEmission_UsesMeshAssetContainerWhenDedicatedMaterialSlotIsNotPreserved()
    {
        ResoniteObjectSlotHierarchy objectSlots = new(
            new CreatedSlot(new ResoniteSlotLocator("asset-lod-slot"), "Asset LOD"),
            new CreatedSlot(new ResoniteSlotLocator("lod-slot"), "LOD"),
            "Triangle Object",
            new PlateauResoniteLink.Targets.Resonite.ResoniteFloat3(0.0, 0.0, 0.0),
            null);
        PlannedDedicatedMaterialAsset dedicatedMaterial = new(
            new ResoniteMaterialBinding(
                BaseColor: new PlateauResoniteLink.Targets.Resonite.ResoniteColor(1.0, 1.0, 1.0, 1.0),
                MaterialType: ResoniteMaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                Projection: ResoniteMaterialProjection.Uv,
                DepthOffset: null,
                SubmeshIndices: [0],
                ResoniteMaterialAssetBinding.Presentation),
            [
                new PlannedTextureAsset(ResoniteSceneMaterialConventions.PlannedTextureRole.Albedo, new Uri("resdb:///texture/albedo")),
                new PlannedTextureAsset(ResoniteSceneMaterialConventions.PlannedTextureRole.Normal, new Uri("resdb:///texture/normal")),
                new PlannedTextureAsset(ResoniteSceneMaterialConventions.PlannedTextureRole.Height, new Uri("resdb:///texture/height")),
                new PlannedTextureAsset(ResoniteSceneMaterialConventions.PlannedTextureRole.Metallic, new Uri("resdb:///texture/metallic")),
                new PlannedTextureAsset(ResoniteSceneMaterialConventions.PlannedTextureRole.Emission, new Uri("resdb:///texture/emission")),
            ],
            PreserveDedicatedMaterialSlot: false);
        PlannedBatchEmission batchPlan = CreateBatchPlan(
            objectSlots,
            new PlannedTriangleMeshGeometryAsset(
                "Triangle Object",
                new Uri("resdb:///mesh/triangle")),
            [dedicatedMaterial],
            [new PlannedDirectRendererMaterialBinding(dedicatedMaterial)],
            true);

        PlannedBatchComponentEmission materialComponent = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal));
        PlannedBatchComponentEmission meshRenderer = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal));
        SyncList rendererMaterials = Assert.IsType<SyncList>(ToMember(meshRenderer.Members["Materials"]));
        Reference materialReference = Assert.IsType<Reference>(Assert.Single(rendererMaterials.Elements));
        Assert.Equal(ToPlannedTargetId(materialComponent), materialReference.TargetID);

        PlannedBatchComponentEmission[] materialTextures = batchPlan.ComponentEmissions
            .Where(component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(5, materialTextures.Length);

        IReadOnlyDictionary<string, PlannedBatchComponentEmission> texturesByUri = materialTextures.ToDictionary(
            static texture => Assert.IsType<PlannedLiteralMember>(texture.Members["URL"]).Value is Field_Uri uri
                ? uri.Value.ToString()
                : throw new InvalidOperationException("Texture URL member was not a URI literal."),
            static texture => texture,
            StringComparer.Ordinal);

        Assert.Equal(ToPlannedTargetId(texturesByUri["resdb:///texture/albedo"]), Assert.IsType<Reference>(ToMember(materialComponent.Members["AlbedoTexture"])).TargetID);
        Assert.Equal(ToPlannedTargetId(texturesByUri["resdb:///texture/normal"]), Assert.IsType<Reference>(ToMember(materialComponent.Members["NormalMap"])).TargetID);
        Assert.Equal(ToPlannedTargetId(texturesByUri["resdb:///texture/height"]), Assert.IsType<Reference>(ToMember(materialComponent.Members["HeightMap"])).TargetID);
        Assert.Equal(ToPlannedTargetId(texturesByUri["resdb:///texture/metallic"]), Assert.IsType<Reference>(ToMember(materialComponent.Members["MetallicMap"])).TargetID);
        Assert.Equal(ToPlannedTargetId(texturesByUri["resdb:///texture/metallic"]), Assert.IsType<Reference>(ToMember(materialComponent.Members["OcclusionMap"])).TargetID);
        Assert.Equal(ToPlannedTargetId(texturesByUri["resdb:///texture/emission"]), Assert.IsType<Reference>(ToMember(materialComponent.Members["EmissiveMap"])).TargetID);
        Assert.DoesNotContain("PreferredProfile", texturesByUri["resdb:///texture/albedo"].Members.Keys);
        Assert.Equal("Linear", Assert.IsType<Field_Nullable_Enum>(ToMember(texturesByUri["resdb:///texture/normal"].Members["PreferredProfile"])).Value);
        Assert.Equal("Linear", Assert.IsType<Field_Nullable_Enum>(ToMember(texturesByUri["resdb:///texture/height"].Members["PreferredProfile"])).Value);
        Assert.Equal("Linear", Assert.IsType<Field_Nullable_Enum>(ToMember(texturesByUri["resdb:///texture/metallic"].Members["PreferredProfile"])).Value);
        Assert.DoesNotContain("PreferredProfile", texturesByUri["resdb:///texture/emission"].Members.Keys);
    }

    [Fact]
    public void CreatePlannedBatchEmission_UsesMainTexturePropertyBlockForRendererOverride()
    {
        ResoniteObjectSlotHierarchy objectSlots = new(
            new CreatedSlot(new ResoniteSlotLocator("asset-lod-slot"), "Asset LOD"),
            new CreatedSlot(new ResoniteSlotLocator("lod-slot"), "LOD"),
            "Triangle Object",
            new PlateauResoniteLink.Targets.Resonite.ResoniteFloat3(0.0, 0.0, 0.0),
            null);
        PlannedReusableMaterialAsset reusableMaterial = new(new ResoniteComponentLocator("shared-material-id"));
        PlannedBatchEmission batchPlan = CreateBatchPlan(
            objectSlots,
            new PlannedTriangleMeshGeometryAsset(
                "Triangle Object",
                new Uri("resdb:///mesh/triangle")),
            [reusableMaterial],
            [
                new PlannedAlbedoMainTextureOverrideRendererMaterialBinding(
                    reusableMaterial,
                    new PlannedTextureAsset(ResoniteSceneMaterialConventions.PlannedTextureRole.Albedo, new Uri("resdb:///texture/override"))),
            ],
            false);

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
        PlannedBatchComponentEmission overrideTexture = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal));

        Assert.Equal("shared-material-id", materialReference.TargetID);
        Assert.Equal(ToPlannedTargetId(propertyBlockComponent), propertyBlockReference.TargetID);
        Assert.Equal(AssertPlanned(meshRenderer.ContainerTarget), AssertPlanned(propertyBlockComponent.ContainerTarget));
        Assert.Equal(ToPlannedTargetId(overrideTexture), Assert.IsType<Reference>(ToMember(propertyBlockComponent.Members["Texture"])).TargetID);
        Assert.DoesNotContain("WrapModeU", overrideTexture.Members.Keys);
        Assert.DoesNotContain("WrapModeV", overrideTexture.Members.Keys);
        Assert.DoesNotContain("PreferredProfile", overrideTexture.Members.Keys);
    }

    [Fact]
    public void CreatePlannedBatchEmission_ClampsTerrainMainTextureOverride()
    {
        ResoniteObjectSlotHierarchy objectSlots = new(
            new CreatedSlot(new ResoniteSlotLocator("asset-lod-slot"), "Asset LOD"),
            new CreatedSlot(new ResoniteSlotLocator("lod-slot"), "LOD"),
            "Triangle Object",
            new PlateauResoniteLink.Targets.Resonite.ResoniteFloat3(0.0, 0.0, 0.0),
            null);
        PlannedReusableMaterialAsset reusableMaterial = new(new ResoniteComponentLocator("shared-material-id"));
        PlannedBatchEmission batchPlan = CreateBatchPlan(
            objectSlots,
            new PlannedTriangleMeshGeometryAsset(
                "Triangle Object",
                new Uri("resdb:///mesh/triangle")),
            [reusableMaterial],
            [
                new PlannedTerrainMainTextureOverrideRendererMaterialBinding(
                    reusableMaterial,
                    new PlannedTextureAsset(ResoniteSceneMaterialConventions.PlannedTextureRole.Albedo, new Uri("resdb:///texture/override"))),
            ],
            false);

        PlannedBatchComponentEmission overrideTexture = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal));

        Assert.Equal("Clamp", Assert.IsType<Field_Enum>(ToMember(overrideTexture.Members["WrapModeU"])).Value);
        Assert.Equal("Clamp", Assert.IsType<Field_Enum>(ToMember(overrideTexture.Members["WrapModeV"])).Value);
        Assert.DoesNotContain("PreferredProfile", overrideTexture.Members.Keys);
    }

    [Fact]
    public void CreatePlannedBatchEmission_UsesSharedTerrainPropertyBlockWhenProvided()
    {
        ResoniteObjectSlotHierarchy objectSlots = new(
            new CreatedSlot(new ResoniteSlotLocator("asset-lod-slot"), "Asset LOD"),
            new CreatedSlot(new ResoniteSlotLocator("lod-slot"), "LOD"),
            "Triangle Object",
            new PlateauResoniteLink.Targets.Resonite.ResoniteFloat3(0.0, 0.0, 0.0),
            null);
        PlannedReusableMaterialAsset reusableMaterial = new(new ResoniteComponentLocator("shared-material-id"));
        PlannedBatchEmission batchPlan = CreateBatchPlan(
            objectSlots,
            new PlannedTriangleMeshGeometryAsset(
                "Triangle Object",
                new Uri("resdb:///mesh/triangle")),
            [reusableMaterial],
            [
                new PlannedTerrainMainTextureOverrideRendererMaterialBinding(
                    reusableMaterial,
                    new PlannedTextureAsset(ResoniteSceneMaterialConventions.PlannedTextureRole.Albedo, new Uri("resdb:///texture/override")),
                    SharedMainTextureComponent: new ResoniteComponentLocator("shared-terrain-texture-id"),
                    SharedMainTexturePropertyBlockComponent: new ResoniteComponentLocator("shared-terrain-property-block-id")),
            ],
            false);

        PlannedBatchComponentEmission meshRenderer = Assert.Single(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal));
        SyncList rendererPropertyBlocks = Assert.IsType<SyncList>(ToMember(meshRenderer.Members["MaterialPropertyBlocks"]));
        Reference propertyBlockReference = Assert.IsType<Reference>(Assert.Single(rendererPropertyBlocks.Elements));

        Assert.Equal("shared-terrain-property-block-id", propertyBlockReference.TargetID);
        Assert.DoesNotContain(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal));
        Assert.DoesNotContain(
            batchPlan.ComponentEmissions,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal));
    }

    [Fact]
    public void CreatePlannedBatchEmission_UsesDistinctOverrideComponentIdsForSharedMaterialOverrides()
    {
        ResoniteObjectSlotHierarchy objectSlots = new(
            new CreatedSlot(new ResoniteSlotLocator("asset-lod-slot"), "Asset LOD"),
            new CreatedSlot(new ResoniteSlotLocator("lod-slot"), "LOD"),
            "Triangle Object",
            new PlateauResoniteLink.Targets.Resonite.ResoniteFloat3(0.0, 0.0, 0.0),
            null);
        PlannedReusableMaterialAsset reusableMaterial = new(new ResoniteComponentLocator("shared-material-id"));
        PlannedBatchEmission batchPlan = CreateBatchPlan(
            objectSlots,
            new PlannedTriangleMeshGeometryAsset(
                "Triangle Object",
                new Uri("resdb:///mesh/triangle")),
            [reusableMaterial],
            [
                new PlannedAlbedoMainTextureOverrideRendererMaterialBinding(
                    reusableMaterial,
                    new PlannedTextureAsset(ResoniteSceneMaterialConventions.PlannedTextureRole.Albedo, new Uri("resdb:///texture/override-a"))),
                new PlannedAlbedoMainTextureOverrideRendererMaterialBinding(
                    reusableMaterial,
                    new PlannedTextureAsset(ResoniteSceneMaterialConventions.PlannedTextureRole.Albedo, new Uri("resdb:///texture/override-b"))),
            ],
            false);

        PlannedBatchComponentEmission[] propertyBlocks = batchPlan.ComponentEmissions
            .Where(static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal))
            .ToArray();
        PlannedBatchComponentEmission[] textures = batchPlan.ComponentEmissions
            .Where(static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(2, propertyBlocks.Length);
        Assert.Equal(2, textures.Length);
        Assert.Equal(2, propertyBlocks.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(2, textures.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(
            2,
            propertyBlocks
                .Select(component => Assert.IsType<Reference>(ToMember(component.Members["Texture"])).TargetID)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    private static PlannedBatchSlotEmission AssertPlanned(PlannedSlotTargetReference target)
    {
        return Assert.IsType<PlannedSlotTargetReference.BatchSlotTarget>(target).Slot;
    }

    private static PlannedFieldReference AssertPlannedField(PlannedWorldElementReference target)
    {
        return Assert.IsType<PlannedWorldElementReference.BatchFieldElement>(target).Field;
    }

    private static void AssertGradientPoint(Member member, float expectedPosition, int expectedX, int expectedY)
    {
        SyncObject point = Assert.IsType<SyncObject>(member);
        Assert.Equal(expectedPosition, Assert.IsType<Field_float>(point.Members["Position"]).Value);
        Field_int2 value = Assert.IsType<Field_int2>(point.Members["Value"]);
        Assert.Equal(expectedX, value.Value.x);
        Assert.Equal(expectedY, value.Value.y);
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
            PlannedAddressableFieldMember field => field.Value,
            PlannedAddressableReferenceMember addressableReference => new Reference
            {
                ID = ToPlannedTargetId(addressableReference.Field),
                TargetID = ResolveTargetId(addressableReference.Target),
            },
            PlannedSyncListMember list => new SyncList
            {
                Elements = list.Elements.Select(ToMember).ToList(),
            },
            _ => throw new InvalidOperationException($"Unsupported planned member '{member.GetType().Name}'."),
        };
    }

    private static string? ResolveTargetId(PlannedWorldElementReference target)
    {
        return target switch
        {
            PlannedWorldElementReference.CanonicalSlotElement canonicalSlot => canonicalSlot.Locator.Value,
            PlannedWorldElementReference.CanonicalComponentElement canonicalComponent => canonicalComponent.Locator.Value,
            PlannedWorldElementReference.BatchSlotElement plannedSlot => ToPlannedTargetId(plannedSlot.Slot),
            PlannedWorldElementReference.BatchComponentElement plannedComponent => ToPlannedTargetId(plannedComponent.Component),
            PlannedWorldElementReference.BatchFieldElement plannedField => ToPlannedTargetId(plannedField.Field),
            _ => throw new InvalidOperationException($"Unsupported planned world element target '{target.GetType().Name}'."),
        };
    }

    private static string ToPlannedTargetId(object reference)
    {
        return RuntimeHelpers.GetHashCode(reference).ToString(CultureInfo.InvariantCulture);
    }
}
