using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Targets;

[Collection(BundledCompanionTextureIsolationGroup.Name)]
[Trait("Category", "Slow")]
public sealed class ResoniteLinkSceneBuilderTests
{
    private const string DatasetName = "scene-test";
    private const string MeshCode = "53394525";
    private static readonly ResoniteLocalOrigin LocalOrigin = new(35.6875, 139.69375, 0.0);

    [Fact]
    public async Task BuildAsyncImportsGeneratedDemTerrainTexture()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneBuilderRecordingClient client = new();
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            UrlTemplate: "https://example.invalid/{z}/{x}/{y}.png",
            ZoomLevel: 17,
            GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
            MaxTextureSize: 512);
        RecordingTerrainTextureAssetGenerator terrainTextureGenerator = new(
            requestedOverlay => new ResoniteRawTextureImport(
                2,
                2,
                ResoniteTextureColorProfiles.Srgb,
                new byte[16],
                $"terrain-overlay/{requestedOverlay.PackageName}/{requestedOverlay.ZoomLevel}/generated"));
        ResoniteConstructionMetadata metadata = ResoniteLinkSceneBuilderTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetDirectory.Path,
            LocalOrigin,
            packageNames: ["dem"],
            sourceFiles:
            [
                $"udx/dem/533945/plateau_{DatasetName}_dem_533945.gml",
            ],
            terrainTextureOverlays: [overlay]);
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "dem-overlay-object",
            DisplayName: "DEM Overlay Object",
            PackageName: "dem",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLinkSceneBuilderTestSupport.CreateTriangleMesh("dem-overlay-material"),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "dem-overlay-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    TerrainOverlay: overlay),
            ],
            SourceObjectKey: "dem-overlay-source");

        await ResoniteLinkSceneBuilderTestSupport.BuildSceneAsync(
            metadata,
            [cityObject],
            client,
            terrainTextureGenerator);

        Assert.Equal([overlay], terrainTextureGenerator.RequestedOverlays);
        ResoniteRawTextureImport importedTexture = Assert.Single(client.ImportedRawTextures);
        Assert.Equal("terrain-overlay/dem/17/generated", importedTexture.Identity);

        Component material = Assert.Single(
            client.ComponentsById.Values,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal));
        Reference albedoReference = Assert.IsType<Reference>(material.Members["AlbedoTexture"]);
        Component albedoTextureComponent = client.ComponentsById[albedoReference.TargetID];
        Assert.Equal("[FrooxEngine]FrooxEngine.StaticTexture2D", albedoTextureComponent.ComponentType);
        Field_Uri textureUrl = Assert.IsType<Field_Uri>(albedoTextureComponent.Members["URL"]);
        Assert.StartsWith("resdb:///texture/", textureUrl.Value.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsyncSendsHeightMapAsHdrRawTextureAndCreatesGridMesh()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneBuilderRecordingClient client = new();
        ResoniteConstructionMetadata metadata = ResoniteLinkSceneBuilderTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetDirectory.Path,
            LocalOrigin,
            packageNames: ["dem"],
            sourceFiles:
            [
                $"udx/dem/533945/plateau_{DatasetName}_dem_533945.gml",
            ]);
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "heightmap-terrain",
            DisplayName: "HeightMap Terrain",
            PackageName: "dem",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Geometry: new ResoniteHeightMapGridGeometry(
                Width: 2,
                Height: 2,
                Size: new ResoniteFloat2(10.0, 10.0),
                MinHeight: 0.0,
                MaxHeight: 3.0,
                HeightSamples: [0.0, 1.0, 2.0, 3.0]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "wireframe-heightmap",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Wireframe,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: "heightmap-source");

        await ResoniteLinkSceneBuilderTestSupport.BuildSceneAsync(metadata, [cityObject], client);

        ResoniteRawHdrTextureImport importedTexture = Assert.Single(client.ImportedRawHdrTextures);
        Assert.Equal(2, importedTexture.Width);
        Assert.Equal(2, importedTexture.Height);
        float[] pixels = new float[importedTexture.RawRgbaFloatBytes.Length / sizeof(float)];
        Buffer.BlockCopy(importedTexture.RawRgbaFloatBytes, 0, pixels, 0, importedTexture.RawRgbaFloatBytes.Length);
        Assert.Equal(0.0f, pixels[0]);
        Assert.Equal(0.0f, pixels[1]);
        Assert.Equal(3.0f, pixels[2]);
        Assert.Equal(1.0f, pixels[3]);

        Component gridMesh = Assert.Single(
            client.ComponentsById.Values,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.GridMesh", StringComparison.Ordinal));
        Reference displacementTextureReference = Assert.IsType<Reference>(gridMesh.Members["DisplacementTexture"]);
        Component displacementTexture = client.ComponentsById[displacementTextureReference.TargetID];
        Assert.Equal("[FrooxEngine]FrooxEngine.StaticTexture2D", displacementTexture.ComponentType);
    }

    [Fact]
    public async Task BuildAsyncDisablesColliderForNoCollisionCityObjects()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneBuilderRecordingClient client = new();
        ResoniteConstructionMetadata metadata = ResoniteLinkSceneBuilderTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetDirectory.Path,
            LocalOrigin,
            packageNames: ["bldg"],
            sourceFiles:
            [
                $"udx/bldg/{MeshCode}/plateau_{DatasetName}_bldg_{MeshCode}.gml",
            ]);
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "no-collision-object",
            DisplayName: "No Collision Object",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLinkSceneBuilderTestSupport.CreateTriangleMesh("wireframe-material"),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "wireframe-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Wireframe,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            CollisionEnabled: false,
            SourceObjectKey: "no-collision-source");

        await ResoniteLinkSceneBuilderTestSupport.BuildSceneAsync(metadata, [cityObject], client);

        Component collider = Assert.Single(
            client.AddedComponents.Where(request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshCollider", StringComparison.Ordinal)
                    && string.Equals(client.SlotsById[request.ContainerSlotId].Name?.Value, "No Collision Object", StringComparison.Ordinal))
                .Select(static request => request.Data));
        Field_Enum collisionType = Assert.IsType<Field_Enum>(collider.Members["Type"]);
        Field_bool characterCollider = Assert.IsType<Field_bool>(collider.Members["CharacterCollider"]);

        Assert.Equal("NoCollision", collisionType.Value);
        Assert.False(characterCollider.Value);
    }

    [Fact]
    public async Task BuildAsyncPlacesObjectUnderLodHierarchy()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneBuilderRecordingClient client = new();
        string sourceFile = $"udx/bldg/{MeshCode}/plateau_{DatasetName}_bldg_{MeshCode}.gml";
        ResoniteConstructionMetadata metadata = ResoniteLinkSceneBuilderTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetDirectory.Path,
            LocalOrigin,
            packageNames: ["bldg"],
            sourceFiles: [sourceFile]);
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "hierarchy-object",
            DisplayName: "Hierarchy Building",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 2,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLinkSceneBuilderTestSupport.CreateTriangleMesh("hierarchy-material"),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "hierarchy-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Wireframe,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: "hierarchy-source",
            SourceFileRelativePath: sourceFile);

        await ResoniteLinkSceneBuilderTestSupport.BuildSceneAsync(metadata, [cityObject], client);

        AddComponent meshRendererRequest = Assert.Single(
            client.AddedComponents,
            static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal));
        Slot objectSlot = client.SlotsById[meshRendererRequest.ContainerSlotId];
        Slot lodSlot = client.SlotsById[objectSlot.Parent!.TargetID];

        Assert.Equal("Hierarchy Building", objectSlot.Name?.Value);
        Assert.Equal("LOD2", lodSlot.Name?.Value);
    }
}
