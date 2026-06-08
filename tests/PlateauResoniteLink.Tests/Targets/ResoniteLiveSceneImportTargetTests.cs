using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using static PlateauResoniteLink.Tests.TextureImportSourceTestFactory;

namespace PlateauResoniteLink.Tests.Targets;

[Trait("Category", "Slow")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Test logger factories are handed to the import target for the duration of the test.")]
public sealed class ResoniteLiveSceneImportTargetTests
{
    private const string DatasetName = "scene-test";
    private const string MeshCode = "53394525";
    private static readonly ResoniteLocalOrigin LocalOrigin = new(35.6875, 139.69375, 0.0);

    [Fact]
    public async Task ExecuteAsyncImportsGeneratedDemTerrainTextureWithCanvasTransform()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneSinkRecordingClient client = new();
        using FakeTerrainTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator terrainTextureGenerator = new(httpClient, disablePersistentCache: true);
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay(MeshCode, "https://tiles.example/{z}/{x}/{y}.png", maxTextureSize: 4096);
        TerrainTextureLayoutPlan layout = TerrainTextureLayoutPlanner.Create(overlay.GeographicBounds, overlay.ZoomLevel);
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
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
            SlotKey: "dem-overlay-object",
            DisplayName: "DEM Overlay Object",
            PackageName: "dem",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    AssetBinding: ResoniteMaterialAssetBinding.Presentation,
                    TextureScale: null,
                    TextureOffset: null,
                    TerrainOverlayMaterial: new TerrainOverlayMaterialBinding(overlay.MeshCode, overlay)),
            ],
            SourceFileRelativePath: $"udx/dem/533945/plateau_{DatasetName}_dem_533945.gml");

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [cityObject],
            client,
            terrainTextureGenerator);

        RawTexturePayload importedTexture = Assert.Single(
            ImportedRgba32Textures(client),
            texture => texture.Width == RoundUpToPowerOfTwo(layout.CropWidth)
                && texture.Height == RoundUpToPowerOfTwo(layout.CropHeight));
        Assert.Equal(RoundUpToPowerOfTwo(layout.CropWidth), importedTexture.Width);
        Assert.Equal(RoundUpToPowerOfTwo(layout.CropHeight), importedTexture.Height);
        Component meshRenderer = Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal)
                && string.Equals(client.SlotsById[request.ContainerSlotId].Name?.Value, "DEM Overlay Object", StringComparison.Ordinal))
            .Data;
        SyncList materials = Assert.IsType<SyncList>(meshRenderer.Members["Materials"]);
        SyncList propertyBlocks = Assert.IsType<SyncList>(meshRenderer.Members["MaterialPropertyBlocks"]);
        string materialId = Assert.IsType<Reference>(Assert.Single(materials.Elements)).TargetID;
        Component propertyBlock = Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal)).Data;

        Assert.Contains(
            client.ComponentsById.Values,
            component => string.Equals(component.ID, materialId, StringComparison.Ordinal)
                && string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal));
        Assert.Equal("[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", propertyBlock.ComponentType);
        string propertyBlockReferenceId = Assert.IsType<Reference>(Assert.Single(propertyBlocks.Elements)).TargetID;
        AddComponent plannedPropertyBlock = Assert.Single(
            client.AddedComponents,
            operation => string.Equals(operation.Data.ID, propertyBlockReferenceId, StringComparison.Ordinal)
                && string.Equals(operation.Data.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal));

        string overrideTextureReferenceId = Assert.IsType<Reference>(plannedPropertyBlock.Data.Members["Texture"]).TargetID;
        AddComponent overrideTextureOperation = Assert.Single(
            client.AddedComponents,
            operation => string.Equals(operation.Data.ID, overrideTextureReferenceId, StringComparison.Ordinal)
                && string.Equals(operation.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal));
        Component overrideTextureComponent = overrideTextureOperation.Data;
        Assert.Equal("[FrooxEngine]FrooxEngine.StaticTexture2D", overrideTextureComponent.ComponentType);
        Field_Uri textureUrl = Assert.IsType<Field_Uri>(overrideTextureComponent.Members["URL"]);
        Assert.StartsWith("resdb:///texture/", textureUrl.Value.ToString(), StringComparison.Ordinal);
        Assert.Equal("Clamp", Assert.IsType<Field_Enum>(overrideTextureComponent.Members["WrapModeU"]).Value);
        Assert.Equal("Clamp", Assert.IsType<Field_Enum>(overrideTextureComponent.Members["WrapModeV"]).Value);
        Assert.DoesNotContain("PreferredProfile", overrideTextureComponent.Members.Keys);
        ImportMeshRawData importedMesh = Assert.Single(client.ImportedMeshes);
        Assert.Equal(3, importedMesh.VertexCount);
        int canvasWidth = RoundUpToPowerOfTwo(layout.CropWidth);
        int canvasHeight = RoundUpToPowerOfTwo(layout.CropHeight);
        float occupiedOffsetX = ((canvasWidth - layout.CropWidth) / 2.0f) / canvasWidth;
        int drawOffsetY = (canvasHeight - layout.CropHeight) / 2;
        float occupiedOffsetY = (float)(canvasHeight - (drawOffsetY + layout.CropHeight)) / canvasHeight;
        Assert.Equal(occupiedOffsetX + ((float)layout.CropWidth / canvasWidth), importedMesh.AccessUV_2D(0)[1].x, 6);
        Assert.Equal(occupiedOffsetY, importedMesh.AccessUV_2D(0)[1].y, 6);
        Assert.Equal(occupiedOffsetX, importedMesh.AccessUV_2D(0)[2].x, 6);
        Assert.Equal(occupiedOffsetY + ((float)layout.CropHeight / canvasHeight), importedMesh.AccessUV_2D(0)[2].y, 6);
    }

    [Fact]
    public async Task ExecuteAsyncSharesTerrainMainTextureComponentByThirdMeshCodeAcrossTerrainAndRoofWithoutOverlayUriOrRoleInKey()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneSinkRecordingClient client = new();
        TerrainTextureOverlay demOverlay = CreateThirdMeshOverlay(MeshCode, "https://tiles.example/dem/{z}/{x}/{y}.png");
        TerrainTextureOverlay roofOverlayWithDifferentUri = CreateThirdMeshOverlay(MeshCode, "https://tiles.example/roof/{z}/{x}/{y}.png");
        RecordingTerrainTextureAssetGenerator terrainTextureGenerator = new(
            requestedOverlay => new GeneratedTerrainTexture(
                CreateRawTextureSource(2, 2, ResoniteTextureColorProfiles.Srgb, new byte[16]),
                new ResoniteFloat2(1.0, 1.0),
                new ResoniteFloat2(0.0, 0.0),
                requestedOverlay.PrimarySource));
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetDirectory.Path,
            LocalOrigin,
            packageNames: ["dem", "bldg"],
            sourceFiles:
            [
                $"udx/dem/533945/plateau_{DatasetName}_dem_533945.gml",
                $"udx/bldg/{MeshCode}/plateau_{DatasetName}_bldg_{MeshCode}.gml",
            ]);
        ResoniteConstructionCityObject terrain = CreateTerrainOverlayCityObject(
            "dem-overlay-shared",
            "DEM Overlay Shared",
            "dem",
            MeshCode,
            demOverlay);
        ResoniteConstructionCityObject roof = CreateTerrainOverlayCityObject(
            "roof-overlay-shared",
            "Roof Overlay Shared",
            "bldg",
            MeshCode,
            roofOverlayWithDifferentUri);
        terrain = WithGenericCommonMaterial(terrain);
        roof = WithGenericCommonMaterial(roof);

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [terrain, roof],
            client,
            terrainTextureGenerator);

        string[] propertyBlockTextureIds = client.AddedComponents
            .Where(static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal))
            .Select(static request => Assert.IsType<Reference>(request.Data.Members["Texture"]).TargetID)
            .ToArray();
        string[] propertyBlockIds = client.AddedComponents
            .Where(static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal))
            .Select(static request => request.Data.ID!)
            .ToArray();
        string[] rendererPropertyBlockIds = client.AddedComponents
            .Where(static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal))
            .Where(request => string.Equals(client.SlotsById[request.ContainerSlotId].Name?.Value, "DEM Overlay Shared", StringComparison.Ordinal)
                || string.Equals(client.SlotsById[request.ContainerSlotId].Name?.Value, "Roof Overlay Shared", StringComparison.Ordinal))
            .Select(request => Assert.IsType<Reference>(Assert.Single(Assert.IsType<SyncList>(request.Data.Members["MaterialPropertyBlocks"]).Elements)).TargetID)
            .ToArray();
        string[] rendererMaterialIds = client.AddedComponents
            .Where(static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal))
            .Where(request => string.Equals(client.SlotsById[request.ContainerSlotId].Name?.Value, "DEM Overlay Shared", StringComparison.Ordinal)
                || string.Equals(client.SlotsById[request.ContainerSlotId].Name?.Value, "Roof Overlay Shared", StringComparison.Ordinal))
            .Select(request => Assert.IsType<Reference>(Assert.Single(Assert.IsType<SyncList>(request.Data.Members["Materials"]).Elements)).TargetID)
            .ToArray();

        Assert.Equal(2, terrainTextureGenerator.RequestedOverlays.Count);
        Assert.Single(propertyBlockTextureIds);
        Assert.Single(propertyBlockIds);
        Assert.Equal(2, rendererPropertyBlockIds.Length);
        Assert.Single(rendererPropertyBlockIds.Distinct(StringComparer.Ordinal));
        Assert.Equal(propertyBlockIds[0], Assert.Single(rendererPropertyBlockIds.Distinct(StringComparer.Ordinal)));
        Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ID, propertyBlockTextureIds[0], StringComparison.Ordinal)
                && string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal));
        Assert.Equal(2, rendererMaterialIds.Length);
        string materialId = Assert.Single(rendererMaterialIds.Distinct(StringComparer.Ordinal));
        Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ID, materialId, StringComparison.Ordinal)
                && string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsyncCreatesSeparateTerrainMainTextureComponentsForDifferentThirdMeshCodes()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneSinkRecordingClient client = new();
        TerrainTextureOverlay firstOverlay = CreateThirdMeshOverlay("53394525", "https://tiles.example/{z}/{x}/{y}.png");
        TerrainTextureOverlay secondOverlay = CreateThirdMeshOverlay("53394526", "https://tiles.example/{z}/{x}/{y}.png");
        RecordingTerrainTextureAssetGenerator terrainTextureGenerator = new(
            requestedOverlay => new GeneratedTerrainTexture(
                CreateRawTextureSource(2, 2, ResoniteTextureColorProfiles.Srgb, new byte[16]),
                new ResoniteFloat2(1.0, 1.0),
                new ResoniteFloat2(0.0, 0.0),
                requestedOverlay.PrimarySource));
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetDirectory.Path,
            LocalOrigin,
            packageNames: ["dem"],
            sourceFiles:
            [
                $"udx/dem/533945/plateau_{DatasetName}_dem_533945.gml",
            ]);
        ResoniteConstructionCityObject first = CreateTerrainOverlayCityObject(
            "terrain-53394525",
            "Terrain 53394525",
            "dem",
            "53394525",
            firstOverlay);
        ResoniteConstructionCityObject second = CreateTerrainOverlayCityObject(
            "terrain-53394526",
            "Terrain 53394526",
            "dem",
            "53394526",
            secondOverlay);

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [first, second],
            client,
            terrainTextureGenerator);

        AddComponent[] sharedTextures = client.AddedComponents
            .Where(static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal))
            .Where(static request => Assert.IsType<Field_Uri>(request.Data.Members["URL"]).Value.ToString().StartsWith("resdb:///texture/", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(2, sharedTextures.Length);
        Assert.Equal(
            ["53394525", "53394526"],
            terrainTextureGenerator.RequestedOverlays.Select(static overlay => overlay.MeshCode.Value).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task ExecuteAsyncUsesExistingSharedTerrainTextureComponentForDedicatedTerrainOverlayMaterial()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneSinkRecordingClient client = new();
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay(MeshCode, "https://tiles.example/{z}/{x}/{y}.png");
        RecordingTerrainTextureAssetGenerator terrainTextureGenerator = new(
            requestedOverlay => new GeneratedTerrainTexture(
                CreateRawTextureSource(2, 2, ResoniteTextureColorProfiles.Srgb, new byte[16]),
                new ResoniteFloat2(1.0, 1.0),
                new ResoniteFloat2(0.0, 0.0),
                requestedOverlay.PrimarySource));
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetDirectory.Path,
            LocalOrigin,
            packageNames: ["dem"],
            sourceFiles:
            [
                $"udx/dem/533945/plateau_{DatasetName}_dem_533945.gml",
            ]);
        ResoniteConstructionCityObject sharedTerrain = CreateTerrainOverlayCityObject(
            "terrain-existing-shared",
            "Terrain Existing Shared",
            "dem",
            MeshCode,
            overlay);

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [sharedTerrain],
            client,
            terrainTextureGenerator);

        AddComponent sharedTexture = Assert.Single(
            client.AddedComponents,
            static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal));
        AddComponent sharedPropertyBlock = Assert.Single(
            client.AddedComponents,
            static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal));
        Uri originalTextureUri = Assert.IsType<Field_Uri>(sharedTexture.Data.Members["URL"]).Value;
        int addedComponentCountBeforeDedicatedRun = client.AddedComponents.Count;
        ResoniteConstructionCityObject dedicatedTerrain = sharedTerrain with
        {
            SlotKey = "terrain-existing-dedicated",
            DisplayName = "Terrain Existing Dedicated",
            Materials =
            [
                sharedTerrain.Materials[0] with
                {
                    BaseColor = new ResoniteColor(0.75, 0.75, 0.75, 1.0),
                },
            ],
        };

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [dedicatedTerrain],
            client,
            terrainTextureGenerator);

        Assert.DoesNotContain(
            client.AddedComponents.Skip(addedComponentCountBeforeDedicatedRun),
            static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal));
        Component dedicatedRenderer = Assert.Single(
            client.AddedComponents.Skip(addedComponentCountBeforeDedicatedRun),
            request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal)
                && string.Equals(client.SlotsById[request.ContainerSlotId].Name?.Value, "Terrain Existing Dedicated", StringComparison.Ordinal))
            .Data;
        string dedicatedPropertyBlockId = Assert.IsType<Reference>(
            Assert.Single(Assert.IsType<SyncList>(dedicatedRenderer.Members["MaterialPropertyBlocks"]).Elements)).TargetID;
        Assert.Equal(sharedPropertyBlock.Data.ID, dedicatedPropertyBlockId);
        Assert.Equal(sharedTexture.Data.ID, Assert.IsType<Reference>(sharedPropertyBlock.Data.Members["Texture"]).TargetID);
        Assert.DoesNotContain(
            client.AddedComponents.Skip(addedComponentCountBeforeDedicatedRun),
            static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal));
        UpdateComponent textureRefresh = Assert.Single(
            client.UpdatedComponents,
            request => string.Equals(request.Data.ID, sharedTexture.Data.ID, StringComparison.Ordinal));
        Field_Uri refreshedUrl = Assert.IsType<Field_Uri>(textureRefresh.Data.Members["URL"]);
        Assert.StartsWith("resdb:///texture/", refreshedUrl.Value.ToString(), StringComparison.Ordinal);
        Assert.NotEqual(originalTextureUri, refreshedUrl.Value);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsTerrainOverlayMaterialWhenMeshCodeBoundsDoNotMatchOverlay()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneSinkRecordingClient client = new();
        TerrainTextureOverlay mismatchedOverlay = CreateThirdMeshOverlay("53394526", "https://tiles.example/{z}/{x}/{y}.png");
        RecordingTerrainTextureAssetGenerator terrainTextureGenerator = new(
            requestedOverlay => new GeneratedTerrainTexture(
                CreateRawTextureSource(2, 2, ResoniteTextureColorProfiles.Srgb, new byte[16]),
                new ResoniteFloat2(1.0, 1.0),
                new ResoniteFloat2(0.0, 0.0),
                requestedOverlay.PrimarySource));
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetDirectory.Path,
            LocalOrigin,
            packageNames: ["dem"],
            sourceFiles:
            [
                $"udx/dem/533945/plateau_{DatasetName}_dem_533945.gml",
            ]);
        ResoniteConstructionCityObject cityObject = CreateTerrainOverlayCityObject(
            "terrain-mismatched-overlay",
            "Terrain Mismatched Overlay",
            "dem",
            MeshCode,
            mismatchedOverlay);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
                metadata,
                [cityObject],
                client,
                terrainTextureGenerator));
        Assert.Contains("matches the overlay geographic bounds", exception.Message, StringComparison.Ordinal);
        Assert.Contains("object_slot='terrain-mismatched-overlay'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"actual_mesh_code='{MeshCode}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("terrain_mesh='53394525'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("sources='tile:17:https://tiles.example/{z}/{x}/{y}.png'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsyncDoesNotReuseExistingGenericCommonMaterialForTerrainOverlayMaterial()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneSinkRecordingClient client = new();
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay(MeshCode, "https://example.invalid/{z}/{x}/{y}.png");
        RecordingTerrainTextureAssetGenerator terrainTextureGenerator = new(
            requestedOverlay => new GeneratedTerrainTexture(
                CreateRawTextureSource(
                    2,
                    2,
                    ResoniteTextureColorProfiles.Srgb,
                    new byte[16]),
                new ResoniteFloat2(1.0, 1.0),
                new ResoniteFloat2(0.125, 0.375),
                requestedOverlay.PrimarySource));
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetDirectory.Path,
            LocalOrigin,
            packageNames: ["dem"],
            sourceFiles:
            [
                $"udx/dem/533945/plateau_{DatasetName}_dem_533945.gml",
            ]);
        SeededCommonMaterialComponent seededCommonMaterial = await SeedCommonMaterialComponentAsync(
            client,
            familySlotName: "generic",
            materialSlotName: "uv",
            componentType: "[FrooxEngine]FrooxEngine.PBS_Metallic");
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "dem-overlay-current-object",
            DisplayName: "DEM Overlay Current Object",
            PackageName: "dem",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    AssetBinding: ResoniteMaterialAssetBinding.Presentation,
                    TerrainOverlayMaterial: new TerrainOverlayMaterialBinding(overlay.MeshCode, overlay)),
            ],
            SourceFileRelativePath: $"udx/dem/533945/plateau_{DatasetName}_dem_533945.gml");

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [cityObject],
            client,
            terrainTextureGenerator);

        Component meshRenderer = Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal)
                && string.Equals(client.SlotsById[request.ContainerSlotId].Name?.Value, "DEM Overlay Current Object", StringComparison.Ordinal))
            .Data;
        string materialId = Assert.IsType<Reference>(Assert.Single(Assert.IsType<SyncList>(meshRenderer.Members["Materials"]).Elements)).TargetID;

        Assert.NotEqual(seededCommonMaterial.ComponentId, materialId);
        Assert.Contains(
            client.ComponentsById.Values,
            component => string.Equals(component.ID, materialId, StringComparison.Ordinal));
        AddComponent propertyBlock = Assert.Single(
            client.AddedComponents,
            static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal));
        AddComponent sharedTexture = Assert.Single(
            client.AddedComponents,
            static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal));
        Assert.Equal(sharedTexture.Data.ID, Assert.IsType<Reference>(propertyBlock.Data.Members["Texture"]).TargetID);
    }

    [Fact]
    public async Task ExecuteAsyncSendsTerrainGridDisplacementAsHdrRawTextureAndCreatesGridMesh()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneSinkRecordingClient client = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
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
            SlotKey: "terrain-grid-terrain",
            DisplayName: "Terrain Grid Terrain",
            PackageName: "dem",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Geometry: new ResoniteTerrainGridGeometry(
                Width: 2,
                Height: 2,
                Size: new ResoniteFloat2(10.0, 10.0),
                MinHeight: 0.0,
                MaxHeight: 3.0,
                HeightSamples: [0.0, 1.0, 2.0, 3.0]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Wireframe,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    ResoniteMaterialAssetBinding.Presentation),
            ],
            SourceFileRelativePath: $"udx/dem/533945/plateau_{DatasetName}_dem_533945.gml");

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(metadata, [cityObject], client);

        RawTexturePayload importedTexture = Assert.Single(ImportedHdrTextures(client));
        Assert.Equal(2, importedTexture.Width);
        Assert.Equal(2, importedTexture.Height);
        float[] pixels = new float[importedTexture.Bytes.Length / sizeof(float)];
        Buffer.BlockCopy(importedTexture.Bytes, 0, pixels, 0, importedTexture.Bytes.Length);
        Assert.Equal(0.0f, pixels[0]);
        Assert.Equal(0.0f, pixels[1]);
        Assert.Equal(3.0f, pixels[2]);
        Assert.Equal(1.0f, pixels[3]);

        Component gridMesh = Assert.Single(
            client.ComponentsById.Values,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.GridMesh", StringComparison.Ordinal));
        Component pointsGradientDriver = Assert.Single(
            client.ComponentsById.Values,
            static component => component.ComponentType.Contains("ValueGradientDriver", StringComparison.Ordinal));
        Component pointsProgressDriver = Assert.Single(
            client.ComponentsById.Values,
            static component => component.ComponentType.Contains("DynamicValueVariableDriver", StringComparison.Ordinal));
        Reference displacementTextureReference = Assert.IsType<Reference>(gridMesh.Members["DisplacementTexture"]);
        Component displacementTexture = client.ComponentsById[displacementTextureReference.TargetID];
        Assert.Equal("[FrooxEngine]FrooxEngine.StaticTexture2D", displacementTexture.ComponentType);
        Assert.Equal("Clamp", Assert.IsType<Field_Enum>(displacementTexture.Members["WrapModeU"]).Value);
        Assert.Equal("Clamp", Assert.IsType<Field_Enum>(displacementTexture.Members["WrapModeV"]).Value);
        Assert.Equal("Point", Assert.IsType<Field_Nullable_Enum>(displacementTexture.Members["FilterMode"]).Value);
        Assert.False(Assert.IsType<Field_bool>(displacementTexture.Members["MipMaps"]).Value);
        Field_int2 gridPoints = Assert.IsType<Field_int2>(gridMesh.Members["Points"]);
        Assert.Equal(gridPoints.ID, Assert.IsType<Reference>(pointsGradientDriver.Members["Target"]).TargetID);
        Field_float progress = Assert.IsType<Field_float>(pointsGradientDriver.Members["Progress"]);
        Assert.Equal(1.0f, progress.Value);
        SyncList gradientPoints = Assert.IsType<SyncList>(pointsGradientDriver.Members["Points"]);
        Assert.Equal(2, gradientPoints.Elements.Count);
        AssertGradientPoint(gradientPoints.Elements[0], 0.0f, 2, 2);
        AssertGradientPoint(gradientPoints.Elements[1], 1.0f, 2, 2);
        Assert.Equal("PLATEAU.Terrain.Grid.Detail", Assert.IsType<Field_string>(pointsProgressDriver.Members["VariableName"]).Value);
        Assert.Equal(progress.ID, Assert.IsType<Reference>(pointsProgressDriver.Members["Target"]).TargetID);
        Assert.Equal(1.0f, Assert.IsType<Field_float>(pointsProgressDriver.Members["DefaultValue"]).Value);
        Assert.DoesNotContain(
            client.ComponentsById.Values,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsyncCreatesDistinctTerrainGridPointsFieldIdsForMultipleDemGrids()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneSinkRecordingClient client = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetDirectory.Path,
            LocalOrigin,
            packageNames: ["dem"],
            sourceFiles:
            [
                $"udx/dem/533945/plateau_{DatasetName}_dem_533945.gml",
            ]);
        ResoniteConstructionCityObject first = CreateTerrainGridCityObject("terrain-grid-a", "Terrain Grid A");
        ResoniteConstructionCityObject second = CreateTerrainGridCityObject("terrain-grid-b", "Terrain Grid B");

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(metadata, [first, second], client);

        List<AddComponent> gridMeshRequests = client.AddedComponents
            .Where(static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.GridMesh", StringComparison.Ordinal))
            .ToList();
        List<AddComponent> pointsGradientDriverRequests = client.AddedComponents
            .Where(static request => request.Data.ComponentType.Contains("ValueGradientDriver", StringComparison.Ordinal))
            .ToList();
        List<AddComponent> pointsProgressDriverRequests = client.AddedComponents
            .Where(static request => request.Data.ComponentType.Contains("DynamicValueVariableDriver", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, gridMeshRequests.Count);
        Assert.Equal(2, pointsGradientDriverRequests.Count);
        Assert.Equal(2, pointsProgressDriverRequests.Count);
        Assert.Equal(2, ImportedHdrTextures(client).Count);

        string[] pointsIds = gridMeshRequests
            .Select(static request => Assert.IsType<Field_int2>(request.Data.Members["Points"]).ID)
            .ToArray();
        Assert.All(pointsIds, static id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(2, pointsIds.Distinct(StringComparer.Ordinal).Count());

        string[] gradientDriverTargets = pointsGradientDriverRequests
            .Select(static request => Assert.IsType<Reference>(request.Data.Members["Target"]).TargetID)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] gradientProgressIds = pointsGradientDriverRequests
            .Select(static request => Assert.IsType<Field_float>(request.Data.Members["Progress"]).ID)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] progressDriverTargets = pointsProgressDriverRequests
            .Select(static request => Assert.IsType<Reference>(request.Data.Members["Target"]).TargetID)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(pointsIds.Order(StringComparer.Ordinal).ToArray(), gradientDriverTargets);
        Assert.Equal(gradientProgressIds, progressDriverTargets);
        Assert.All(
            pointsProgressDriverRequests,
            static request => Assert.Equal(
                "PLATEAU.Terrain.Grid.Detail",
                Assert.IsType<Field_string>(request.Data.Members["VariableName"]).Value));
    }

    [Fact]
    public async Task ExecuteAsyncCreatesDynamicTerrainStaticAndGridAssetsWithGridFallback()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        using SceneSinkRecordingClient client = new();
        List<string> progressMessages = [];
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
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
            SlotKey: "dynamic-terrain",
            DisplayName: "Dynamic Terrain",
            PackageName: "dem",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Geometry: new ResoniteDynamicTerrainGeometry(
                new ResoniteTriangleMeshGeometry(ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh()),
                new ResoniteTerrainGridGeometry(
                    Width: 2,
                    Height: 2,
                    Size: new ResoniteFloat2(10.0, 10.0),
                    MinHeight: 0.0,
                    MaxHeight: 3.0,
                    HeightSamples: [0.0, 1.0, 2.0, 3.0])),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Wireframe,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    ResoniteMaterialAssetBinding.Presentation),
            ],
            SourceFileRelativePath: $"udx/dem/533945/plateau_{DatasetName}_dem_533945.gml");

        await using ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(
            client,
            loggerFactory: new RecordingLoggerFactory(new RecordingLogger(progressMessages.Add)));
        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [cityObject]);

        Assert.Single(client.ImportedMeshes);
        Assert.Single(ImportedHdrTextures(client));
        int queuedMessageIndex = progressMessages.FindIndex(static message => message.Contains("First city object queued", StringComparison.Ordinal));
        int preparationMessageIndex = progressMessages.FindIndex(static message => message.Contains("City object preparation started", StringComparison.Ordinal));
        Assert.True(queuedMessageIndex >= 0);
        Assert.True(preparationMessageIndex >= 0);
        Assert.True(
            queuedMessageIndex < preparationMessageIndex,
            "Dynamic terrain preparation should start only after the city object is queued for a send lane.");
        Component staticMesh = Assert.Single(
            client.ComponentsById.Values,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.StaticMesh", StringComparison.Ordinal));
        Component gridMesh = Assert.Single(
            client.ComponentsById.Values,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.GridMesh", StringComparison.Ordinal));
        Component meshRenderer = Assert.Single(
            client.ComponentsById.Values,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal));
        Component meshCollider = Assert.Single(
            client.ComponentsById.Values,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MeshCollider", StringComparison.Ordinal));
        AddComponent meshRendererRequest = Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ID, meshRenderer.ID, StringComparison.Ordinal));
        AddComponent meshColliderRequest = Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ID, meshCollider.ID, StringComparison.Ordinal));
        Component displacementTexture = client.ComponentsById[Assert.IsType<Reference>(gridMesh.Members["DisplacementTexture"]).TargetID];
        Component[] meshSwitches = client.ComponentsById.Values
            .Where(static component => component.ComponentType.Contains("BooleanAssetDriver", StringComparison.Ordinal))
            .ToArray();
        Component[] boolDrivers = client.ComponentsById.Values
            .Where(static component => component.ComponentType.Contains("DynamicValueVariableDriver", StringComparison.Ordinal)
                && component.ComponentType.Contains("bool", StringComparison.Ordinal))
            .ToArray();
        AddComponent[] meshSwitchRequests = meshSwitches
            .Select(meshSwitch => Assert.Single(
                client.AddedComponents,
                request => string.Equals(request.Data.ID, meshSwitch.ID, StringComparison.Ordinal)))
            .ToArray();
        AddComponent[] boolDriverRequests = boolDrivers
            .Select(boolDriver => Assert.Single(
                client.AddedComponents,
                request => string.Equals(request.Data.ID, boolDriver.ID, StringComparison.Ordinal)))
            .ToArray();

        Assert.Equal(2, meshSwitches.Length);
        Assert.Equal(2, boolDrivers.Length);
        Assert.Equal("[FrooxEngine]FrooxEngine.StaticTexture2D", displacementTexture.ComponentType);
        Reference rendererMesh = Assert.IsType<Reference>(meshRendererRequest.Data.Members["Mesh"]);
        Reference colliderMesh = Assert.IsType<Reference>(meshColliderRequest.Data.Members["Mesh"]);
        Assert.Equal(gridMesh.ID, rendererMesh.TargetID);
        Assert.Equal(gridMesh.ID, colliderMesh.TargetID);
        foreach (Component meshSwitch in meshSwitches)
        {
            AddComponent meshSwitchRequest = Assert.Single(
                meshSwitchRequests,
                request => string.Equals(request.Data.ID, meshSwitch.ID, StringComparison.Ordinal));
            Field_bool state = Assert.IsType<Field_bool>(meshSwitchRequest.Data.Members["State"]);
            Assert.False(state.Value);
            string targetFieldId = Assert.IsType<Reference>(meshSwitchRequest.Data.Members["Target"]).TargetID;
            Assert.StartsWith("local_field_", targetFieldId, StringComparison.Ordinal);
            Assert.Equal(gridMesh.ID, Assert.IsType<Reference>(meshSwitch.Members["FalseTarget"]).TargetID);
            Assert.Equal(staticMesh.ID, Assert.IsType<Reference>(meshSwitch.Members["TrueTarget"]).TargetID);
        }

        foreach (Component boolDriver in boolDrivers)
        {
            AddComponent boolDriverRequest = Assert.Single(
                boolDriverRequests,
                request => string.Equals(request.Data.ID, boolDriver.ID, StringComparison.Ordinal));
            string targetFieldId = Assert.IsType<Reference>(boolDriverRequest.Data.Members["Target"]).TargetID;
            Assert.StartsWith("local_field_", targetFieldId, StringComparison.Ordinal);
            Assert.Equal("PLATEAU.Terrain.Static.Enabled", Assert.IsType<Field_string>(boolDriver.Members["VariableName"]).Value);
            Assert.False(Assert.IsType<Field_bool>(boolDriverRequest.Data.Members["DefaultValue"]).Value);
        }
    }

    [Fact]
    public async Task ExecuteAsyncAppliesTerrainOverlayUvTransformToGridMeshInsteadOfMaterial()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneSinkRecordingClient client = new();
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay(MeshCode, "https://example.invalid/{z}/{x}/{y}.png");
        RecordingTerrainTextureAssetGenerator terrainTextureGenerator = new(
            requestedOverlay => new GeneratedTerrainTexture(
                CreateRawTextureSource(
                    2,
                    2,
                    ResoniteTextureColorProfiles.Srgb,
                    new byte[16]),
                new ResoniteFloat2(0.5, 0.25),
                new ResoniteFloat2(0.125, 0.375),
                requestedOverlay.PrimarySource));
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
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
            SlotKey: "terrain-grid-overlay-terrain",
            DisplayName: "Terrain Grid Overlay Terrain",
            PackageName: "dem",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Geometry: new ResoniteTerrainGridGeometry(
                Width: 2,
                Height: 2,
                Size: new ResoniteFloat2(10.0, 10.0),
                MinHeight: 0.0,
                MaxHeight: 3.0,
                HeightSamples: [0.0, 1.0, 2.0, 3.0],
                UvScale: new ResoniteFloat2(0.8, 0.5),
                UvOffset: new ResoniteFloat2(0.1, 0.2)),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    AssetBinding: ResoniteMaterialAssetBinding.Presentation,
                    TerrainOverlayMaterial: new TerrainOverlayMaterialBinding(overlay.MeshCode, overlay)),
            ],
            SourceFileRelativePath: $"udx/dem/533945/plateau_{DatasetName}_dem_533945.gml");

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [cityObject],
            client,
            terrainTextureGenerator);

        Component gridMesh = Assert.Single(
            client.ComponentsById.Values,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.GridMesh", StringComparison.Ordinal));
        Field_float2 uvScale = Assert.IsType<Field_float2>(gridMesh.Members["UVScale"]);
        Field_float2 uvOffset = Assert.IsType<Field_float2>(gridMesh.Members["UVOffset"]);

        Assert.Equal(0.4f, uvScale.Value.x, 6);
        Assert.Equal(0.125f, uvScale.Value.y, 6);
        Assert.Equal(0.175f, uvOffset.Value.x, 6);
        Assert.Equal(0.425f, uvOffset.Value.y, 6);
        string[] rendererMaterialIds = client.AddedComponents
            .Where(static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal))
            .SelectMany(static request => Assert.IsType<SyncList>(request.Data.Members["Materials"]).Elements)
            .Select(static material => Assert.IsType<Reference>(material).TargetID)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(rendererMaterialIds);
        Assert.All(
            rendererMaterialIds.Select(id => client.ComponentsById[id]),
            materialComponent =>
            {
                Assert.DoesNotContain("TextureScale", materialComponent.Members.Keys);
                Assert.DoesNotContain("TextureOffset", materialComponent.Members.Keys);
            });
    }

    [Fact]
    public async Task ExecuteAsyncDisablesColliderForNoCollisionCityObjects()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneSinkRecordingClient client = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
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
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Wireframe,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    ResoniteMaterialAssetBinding.Presentation),
            ],
            CollisionEnabled: false,
            SourceFileRelativePath: $"udx/bldg/{MeshCode}/plateau_{DatasetName}_bldg_{MeshCode}.gml");

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(metadata, [cityObject], client);

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
    public void EstimateCityObjectWorkingSetBytesCountsTerrainOverlayCanvasBudget()
    {
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("57403600"),
            UrlTemplate: "https://example.invalid/{z}/{x}/{y}.png",
            ZoomLevel: 19,
            GeographicBounds: new GeographicRectangle(38.25, 38.25833333333333, 140.75, 140.7625),
            MaxTextureSize: 8192);
        ResoniteConstructionCityObject withOverlay = new(
            SlotKey: "dem-overlay-budget",
            DisplayName: "DEM Overlay Budget",
            PackageName: "dem",
            ActualMeshCode: "57403600",
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    AssetBinding: ResoniteMaterialAssetBinding.Presentation,
                    TerrainOverlayMaterial: new TerrainOverlayMaterialBinding(overlay.MeshCode, overlay)),
            ]
        );
        ResoniteConstructionCityObject withoutOverlay = withOverlay with
        {
            Materials =
            [
                withOverlay.Materials[0] with { TerrainOverlayMaterial = null },
            ],
        };

        long overlayEstimate = ResoniteCityObjectWorkingSetEstimator.EstimateBytes(withOverlay);
        long baselineEstimate = ResoniteCityObjectWorkingSetEstimator.EstimateBytes(withoutOverlay);

        Assert.True(overlayEstimate > baselineEstimate);
        Assert.True(overlayEstimate - baselineEstimate >= 100L * 1024L * 1024L);
    }

    [Fact]
    public void EstimateCityObjectWorkingSetBytesKeepsOriginalVertexFootprintForUvBake()
    {
        ResoniteImportedMesh sparseMesh = new(
            [
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(5.0, 5.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(6.0, 5.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(7.0, 5.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
            ],
            [
                new ResoniteMeshSubmesh(0, [0, 1, 2]),
            ]);
        ResoniteConstructionCityObject baseline = new(
            SlotKey: "uv-bake-budget-baseline",
            DisplayName: "UV Bake Budget Baseline",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 2,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: sparseMesh,
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: new RawRgba32ResoniteTexturePayload(1, 1, "srgb", [255, 255, 255, 255], "textures/uv-bake-budget.png"),
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                                        AssetBinding: ResoniteMaterialAssetBinding.Presentation,
                    TextureScale: null,
                    TextureOffset: null
                    ),
            ]
        );
        ResoniteConstructionCityObject withBake = baseline with
        {
            SlotKey = "uv-bake-budget-baked",
            DisplayName = "UV Bake Budget Baked",
            Materials =
            [
                baseline.Materials[0] with
                {
                    TextureScale = new ResoniteFloat2(2.0, 0.5),
                    TextureOffset = new ResoniteFloat2(0.25, 0.75),
                },
            ],
        };

        long baselineEstimate = ResoniteCityObjectWorkingSetEstimator.EstimateBytes(baseline);
        long bakedEstimate = ResoniteCityObjectWorkingSetEstimator.EstimateBytes(withBake);

        Assert.True(bakedEstimate > baselineEstimate);
    }

    [Fact]
    public async Task ExecuteAsyncResolvesPlannedIdsBeforeBatchExecution()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneSinkRecordingClient client = new();
        string sourceFile = $"udx/bldg/{MeshCode}/plateau_{DatasetName}_bldg_{MeshCode}.gml";
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetDirectory.Path,
            LocalOrigin,
            packageNames: ["bldg"],
            sourceFiles: [sourceFile]);
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "planned-id-check",
            DisplayName: "Planned Id Check",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: CreateTwoSubmeshMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                                        AssetBinding: ResoniteMaterialAssetBinding.Presentation,
                    Family: BundledDefaultMaterialFamilies.RoadUv),
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(255, 0, 0, "payload/albedo"),
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    ResoniteMaterialAssetBinding.Presentation),
            ],
            SourceFileRelativePath: sourceFile);

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(metadata, [cityObject], client, enableMeshBake: false);

        Assert.All(client.SlotsById.Values, static slot => AssertNoPlannedIds(slot));
        Assert.All(client.ComponentsById.Values, static component => AssertNoPlannedReferences(component.Members.Values));
    }

    [Fact]
    public async Task ExecuteAsyncDoesNotCreateRedundantCompletionMeshCodePlaceholderSlot()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneSinkRecordingClient client = new();
        string sourceFile = $"udx/bldg/{MeshCode}/plateau_{DatasetName}_bldg_{MeshCode}.gml";
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetDirectory.Path,
            LocalOrigin,
            packageNames: ["bldg"],
            sourceFiles: [sourceFile]);
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "placeholder-check",
            DisplayName: "Placeholder Check",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 2,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Wireframe,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    ResoniteMaterialAssetBinding.Presentation),
            ],
            SourceFileRelativePath: sourceFile);

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(metadata, [cityObject], client);

        Slot datasetRoot = Assert.Single(
            client.SlotsById.Values,
            static slot => string.Equals(slot.Name?.Value, $"PLATEAU {DatasetName}", StringComparison.Ordinal));
        string[] directChildNames = client.SlotsById.Values
            .Where(slot => string.Equals(slot.Parent?.TargetID, datasetRoot.ID, StringComparison.Ordinal))
            .Select(static slot => slot.Name?.Value ?? string.Empty)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.DoesNotContain(MeshCode, directChildNames);
        Assert.NotEmpty(directChildNames);
        Assert.Contains(
            client.SlotPaths.Values,
            path => path.EndsWith($"/{Path.GetFileNameWithoutExtension(sourceFile)}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsyncPreservesOriginalNameForNonBakedLod1WhenMeshBakeIsDisabled()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneSinkRecordingClient client = new();
        string sourceFile = $"udx/bldg/{MeshCode}/plateau_{DatasetName}_bldg_{MeshCode}.gml";
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetDirectory.Path,
            LocalOrigin,
            packageNames: ["bldg"],
            sourceFiles: [sourceFile]);
        const string originalName = "Original LOD1 Building";
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "non-baked-name-check",
            DisplayName: originalName,
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 1,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Wireframe,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    ResoniteMaterialAssetBinding.Presentation),
            ],
            SourceFileRelativePath: sourceFile);

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [cityObject],
            client,
            enableMeshBake: false);

        AddComponent meshRendererRequest = Assert.Single(
            client.AddedComponents,
            static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal));
        Slot objectSlot = client.SlotsById[meshRendererRequest.ContainerSlotId];

        Assert.Equal(originalName, objectSlot.Name?.Value);
        Assert.DoesNotContain(
            client.SlotsById.Values,
            static slot => slot.Name?.Value?.StartsWith("MeshBake ", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task ExecuteAsyncNormalizesBundledFamilyUvScaleIntoMeshAndKeepsMaterialLocal()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneSinkRecordingClient client = new();
        string sourceFile = $"udx/bldg/{MeshCode}/plateau_{DatasetName}_bldg_{MeshCode}.gml";
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetDirectory.Path,
            LocalOrigin,
            packageNames: ["bldg"],
            sourceFiles: [sourceFile]);
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "bundled-family-scale-check",
            DisplayName: "Bundled Family Scale Check",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                                        AssetBinding: ResoniteMaterialAssetBinding.Presentation,
                    TextureScale: new ResoniteFloat2(0.5, 0.5),
                    Family: BundledDefaultMaterialFamilies.Facade,
                    BundledVariantIndex: 0),
            ],
            SourceFileRelativePath: sourceFile);

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [cityObject],
            client,
            enableMeshBake: false);

        Component meshRenderer = Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal)
                && string.Equals(client.SlotsById[request.ContainerSlotId].Name?.Value, "Bundled Family Scale Check", StringComparison.Ordinal))
            .Data;
        SyncList materials = Assert.IsType<SyncList>(meshRenderer.Members["Materials"]);
        string materialId = Assert.IsType<Reference>(Assert.Single(materials.Elements)).TargetID;
        IEnumerable<AddComponent> allAddComponents = client.AddedComponents
            .Concat(client.Batches.SelectMany(static operations => operations).OfType<AddComponent>());
        AddComponent materialRequest = Assert.Single(
            allAddComponents,
            request => string.Equals(request.Data.ID, materialId, StringComparison.Ordinal));
        Component sharedMaterial = materialRequest.Data;
        SyncList propertyBlocks = Assert.IsType<SyncList>(meshRenderer.Members["MaterialPropertyBlocks"]);
        ImportMeshRawData importedMesh = Assert.Single(client.ImportedMeshes);
        float expectedUvScaleX = (float)(0.5 / BundledDefaultMaterialProfiles.FacadeDefaultTilesPerMeterValue.X);
        float expectedUvScaleY = (float)(0.5 / BundledDefaultMaterialProfiles.FacadeDefaultTilesPerMeterValue.Y);

        Assert.False(materialRequest.MessageID.StartsWith("setup_common_material_component", StringComparison.Ordinal));
        Assert.DoesNotContain("TextureScale", sharedMaterial.Members.Keys);
        Assert.DoesNotContain("TextureOffset", sharedMaterial.Members.Keys);
        Assert.Empty(propertyBlocks.Elements);
        Assert.Equal(0.0f, importedMesh.AccessUV_2D(0)[0].x, 6);
        Assert.Equal(0.0f, importedMesh.AccessUV_2D(0)[0].y, 6);
        Assert.Equal(expectedUvScaleX, importedMesh.AccessUV_2D(0)[1].x, 6);
        Assert.Equal(0.0f, importedMesh.AccessUV_2D(0)[1].y, 6);
        Assert.Equal(0.0f, importedMesh.AccessUV_2D(0)[2].x, 6);
        Assert.Equal(expectedUvScaleY, importedMesh.AccessUV_2D(0)[2].y, 6);
    }

    [Fact]
    public async Task ExecuteAsyncNormalizesBundledFamilyUvTransformIntoMeshAndAvoidsDedicatedUvTransformEmission()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneSinkRecordingClient client = new();
        string sourceFile = $"udx/bldg/{MeshCode}/plateau_{DatasetName}_bldg_{MeshCode}.gml";
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetDirectory.Path,
            LocalOrigin,
            packageNames: ["bldg"],
            sourceFiles: [sourceFile]);
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "bundled-family-transform-check",
            DisplayName: "Bundled Family Transform Check",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                                        AssetBinding: ResoniteMaterialAssetBinding.Presentation,
                    TextureScale: new ResoniteFloat2(0.5, 0.5),
                    TextureOffset: new ResoniteFloat2(0.125, 0.25),
                    Family: BundledDefaultMaterialFamilies.Facade,
                    BundledVariantIndex: 0),
            ],
            SourceFileRelativePath: sourceFile);

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [cityObject],
            client,
            enableMeshBake: false);

        AddComponent meshRendererRequest = Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal)
                && string.Equals(client.SlotsById[request.ContainerSlotId].Name?.Value, "Bundled Family Transform Check", StringComparison.Ordinal));
        Component meshRenderer = meshRendererRequest.Data;
        SyncList materials = Assert.IsType<SyncList>(meshRenderer.Members["Materials"]);
        string materialId = Assert.IsType<Reference>(Assert.Single(materials.Elements)).TargetID;
        IEnumerable<AddComponent> allAddComponents = client.AddedComponents
            .Concat(client.Batches.SelectMany(static operations => operations).OfType<AddComponent>());
        AddComponent materialRequest = Assert.Single(
            allAddComponents,
            request => string.Equals(request.Data.ID, materialId, StringComparison.Ordinal));
        SyncList propertyBlocks = Assert.IsType<SyncList>(meshRenderer.Members["MaterialPropertyBlocks"]);
        ImportMeshRawData importedMesh = Assert.Single(client.ImportedMeshes);
        float expectedUvScaleX = (float)(0.5 / BundledDefaultMaterialProfiles.FacadeDefaultTilesPerMeterValue.X);
        float expectedUvScaleY = (float)(0.5 / BundledDefaultMaterialProfiles.FacadeDefaultTilesPerMeterValue.Y);
        float expectedUvOffsetX = (float)(0.125 / BundledDefaultMaterialProfiles.FacadeDefaultTilesPerMeterValue.X);
        float expectedMaterialOffsetY = (float)(0.5 / 6.0);
        float expectedUvOffsetY = (float)((0.25 - expectedMaterialOffsetY) / BundledDefaultMaterialProfiles.FacadeDefaultTilesPerMeterValue.Y);

        Assert.False(materialRequest.MessageID.StartsWith("setup_common_material_component", StringComparison.Ordinal));
        Assert.DoesNotContain("TextureScale", materialRequest.Data.Members.Keys);
        Assert.DoesNotContain("TextureOffset", materialRequest.Data.Members.Keys);
        Assert.Empty(propertyBlocks.Elements);
        Assert.Equal(expectedUvOffsetX, importedMesh.AccessUV_2D(0)[0].x, 6);
        Assert.Equal(expectedUvOffsetY, importedMesh.AccessUV_2D(0)[0].y, 6);
        Assert.Equal(expectedUvOffsetX + expectedUvScaleX, importedMesh.AccessUV_2D(0)[1].x, 6);
        Assert.Equal(expectedUvOffsetY, importedMesh.AccessUV_2D(0)[1].y, 6);
        Assert.Equal(expectedUvOffsetX, importedMesh.AccessUV_2D(0)[2].x, 6);
        Assert.Equal(expectedUvOffsetY + expectedUvScaleY, importedMesh.AccessUV_2D(0)[2].y, 6);
    }

    [Fact]
    public void ValidateTriangleMeshBindingsForImportRejectsOutOfRangeMaterialSubmeshAssignment()
    {
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "invalid-submesh-range",
            DisplayName: "Invalid Submesh Range",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    ResoniteMaterialAssetBinding.Presentation),
            ]
        );

        ResoniteMeshValidationException exception = AssertTriangleMeshValidationFailure(cityObject);

        Assert.Contains("targeted missing submesh index 1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("material_bindings=[material#0[1]]", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateTriangleMeshBindingsForImportRejectsDynamicTerrainOutOfRangeMaterialSubmeshAssignment()
    {
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "invalid-dynamic-terrain-submesh-range",
            DisplayName: "Invalid Dynamic Terrain Submesh Range",
            PackageName: "dem",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Geometry: new ResoniteDynamicTerrainGeometry(
                new ResoniteTriangleMeshGeometry(ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh()),
                new ResoniteTerrainGridGeometry(
                    Width: 2,
                    Height: 2,
                    Size: new ResoniteFloat2(10.0, 10.0),
                    MinHeight: 0.0,
                    MaxHeight: 3.0,
                    HeightSamples: [0.0, 1.0, 2.0, 3.0])),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    ResoniteMaterialAssetBinding.Presentation),
            ],
            SourceFileRelativePath: $"udx/dem/533945/plateau_{DatasetName}_dem_533945.gml");

        ResoniteMeshValidationException exception = AssertTriangleMeshValidationFailure(cityObject);

        Assert.Contains("targeted missing submesh index 1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("material_bindings=[material#0[1]]", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateTriangleMeshBindingsForImportRejectsDuplicateMaterialSubmeshAssignment()
    {
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "invalid-submesh-duplicate",
            DisplayName: "Invalid Submesh Duplicate",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: CreateTwoSubmeshMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    ResoniteMaterialAssetBinding.Presentation),
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0, 1],
                    ResoniteMaterialAssetBinding.Presentation),
            ]
        );

        ResoniteMeshValidationException exception = AssertTriangleMeshValidationFailure(cityObject);

        Assert.Contains("assigned submesh index 0", exception.Message, StringComparison.Ordinal);
        Assert.Contains("materials=2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsyncFailsFastOnDuplicateMaterialSubmeshAssignmentBeforeDynamicUvNormalization()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneSinkRecordingClient client = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
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
            SlotKey: "invalid-submesh-duplicate-dynamic",
            DisplayName: "Invalid Submesh Duplicate Dynamic",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: CreateTwoSubmeshMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(255, 0, 0, "textures/duplicate-a.png"),
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                                        AssetBinding: ResoniteMaterialAssetBinding.Presentation,
                    TextureScale: new ResoniteFloat2(2.0, 0.5),
                    TextureOffset: new ResoniteFloat2(0.25, 0.75)),
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(0, 255, 0, "textures/duplicate-b.png"),
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0, 1],
                    ResoniteMaterialAssetBinding.Presentation),
            ]
        );

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(metadata, [cityObject], client, enableMeshBake: false));

        Assert.Contains("assigned submesh index 0", exception.Message, StringComparison.Ordinal);
        Assert.Contains("materials=2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateTriangleMeshBindingsForImportRejectsUnassignedMeshSubmesh()
    {
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "invalid-submesh-unassigned",
            DisplayName: "Invalid Submesh Unassigned",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: CreateTwoSubmeshMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    ResoniteMaterialAssetBinding.Presentation),
            ]
        );

        ResoniteMeshValidationException exception = AssertTriangleMeshValidationFailure(cityObject);

        Assert.Contains("left submesh index 1 without a material assignment", exception.Message, StringComparison.Ordinal);
        Assert.Contains("materials=1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateTriangleMeshBindingsForImportRejectsTriangleMeshWithoutAnySubmesh()
    {
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "invalid-empty-submesh",
            DisplayName: "Invalid Empty Submesh",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: new ResoniteImportedMesh(
                Vertices:
                [
                    new ResoniteMeshVertex(
                        new ResoniteFloat3(0.0, 0.0, 0.0),
                        new ResoniteFloat3(0.0, 1.0, 0.0),
                        new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(
                        new ResoniteFloat3(1.0, 0.0, 0.0),
                        new ResoniteFloat3(0.0, 1.0, 0.0),
                        new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(
                        new ResoniteFloat3(0.0, 0.0, 1.0),
                        new ResoniteFloat3(0.0, 1.0, 0.0),
                        new ResoniteFloat2(0.0, 1.0)),
                ],
                Submeshes: []),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    ResoniteMaterialAssetBinding.Presentation),
            ]
        );

        ResoniteMeshValidationException exception = AssertTriangleMeshValidationFailure(cityObject);

        Assert.Contains("did not contain any submesh", exception.Message, StringComparison.Ordinal);
        Assert.Contains("submeshes=0", exception.Message, StringComparison.Ordinal);
    }

    private static ResoniteImportedMesh CreateTwoSubmeshMesh()
    {
        return new ResoniteImportedMesh(
            Vertices:
            [
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(2.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(3.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(2.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
            ],
            Submeshes:
            [
                new ResoniteMeshSubmesh(0, [0, 1, 2]),
                new ResoniteMeshSubmesh(1, [3, 4, 5]),
            ]);
    }

    private static ResoniteMeshValidationException AssertTriangleMeshValidationFailure(
        ResoniteConstructionCityObject cityObject)
    {
        ResoniteImportedMesh mesh = cityObject.Geometry switch
        {
            ResoniteTriangleMeshGeometry triangleMesh => triangleMesh.Mesh,
            ResoniteDynamicTerrainGeometry dynamicTerrain => dynamicTerrain.StaticMesh.Mesh,
            _ => throw new InvalidOperationException($"Unexpected geometry type '{cityObject.Geometry.GetType().Name}'."),
        };
        return Assert.Throws<ResoniteMeshValidationException>(
            () => ResoniteCityObjectPreparation.ValidateTriangleMeshBindingsForImport(cityObject, mesh));
    }

    private static ResoniteConstructionCityObject CreateTerrainGridCityObject(string slotKey, string displayName)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: displayName,
            PackageName: "dem",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Geometry: new ResoniteTerrainGridGeometry(
                Width: 2,
                Height: 2,
                Size: new ResoniteFloat2(10.0, 10.0),
                MinHeight: 0.0,
                MaxHeight: 3.0,
                HeightSamples: [0.0, 1.0, 2.0, 3.0]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Wireframe,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    ResoniteMaterialAssetBinding.Presentation),
            ],
            SourceFileRelativePath: $"udx/dem/533945/plateau_{DatasetName}_dem_533945.gml");
    }

    private static ResoniteConstructionCityObject CreateTerrainOverlayCityObject(
        string slotKey,
        string displayName,
        string packageName,
        string meshCode,
        TerrainTextureOverlay overlay)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: displayName,
            PackageName: packageName,
            ActualMeshCode: meshCode,
            LodLevel: string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase) ? 0 : 1,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    AssetBinding: ResoniteMaterialAssetBinding.Presentation,
                    TextureScale: null,
                    TextureOffset: null,
                    TerrainOverlayMaterial: new TerrainOverlayMaterialBinding(ThirdRegionalMeshCode.Parse(meshCode), overlay)),
            ],
            SourceFileRelativePath: $"udx/{packageName}/{meshCode}/plateau_{DatasetName}_{packageName}_{meshCode}.gml");
    }

    private static ResoniteConstructionCityObject WithGenericCommonMaterial(ResoniteConstructionCityObject cityObject)
    {
        return cityObject with
        {
            Materials = cityObject.Materials
                .Select(static material => material with
                {
                    AssetBinding = ResoniteMaterialAssetBinding.PresentationCommon(CommonMaterialCatalog.Create().Generic.Uv),
                })
                .ToArray(),
        };
    }

    private static void AssertGradientPoint(Member member, float expectedPosition, int expectedX, int expectedY)
    {
        SyncObject point = Assert.IsType<SyncObject>(member);
        Assert.Equal(expectedPosition, Assert.IsType<Field_float>(point.Members["Position"]).Value);
        Field_int2 value = Assert.IsType<Field_int2>(point.Members["Value"]);
        Assert.Equal(expectedX, value.Value.x);
        Assert.Equal(expectedY, value.Value.y);
    }

    private static void AssertNoPlannedIds(Slot slot)
    {
        Assert.False((slot.ID ?? string.Empty).StartsWith("plan:", StringComparison.Ordinal));
        Assert.False((slot.Parent?.TargetID ?? string.Empty).StartsWith("plan:", StringComparison.Ordinal));
        Assert.False((slot.Tag?.Value ?? string.Empty).StartsWith("plan:", StringComparison.Ordinal));
    }

    private static async Task<SeededCommonMaterialComponent> SeedCommonMaterialComponentAsync(
        SceneSinkRecordingClient client,
        string familySlotName,
        string materialSlotName,
        string componentType)
    {
        string sharedAssetsRootId = (await client.AddSlotAsync(
            new AddSlot
            {
                Data = new Slot
                {
                    Parent = new Reference { TargetID = "Root" },
                    Name = new Field_string { Value = "PLATEAU Shared Assets" },
                },
            },
            CancellationToken.None)).Slot.Value;
        string commonMaterialsRootId = (await client.AddSlotAsync(
            new AddSlot
            {
                Data = new Slot
                {
                    Parent = new Reference { TargetID = sharedAssetsRootId },
                    Name = new Field_string { Value = "Common Materials" },
                },
            },
            CancellationToken.None)).Slot.Value;
        string familySlotId = (await client.AddSlotAsync(
            new AddSlot
            {
                Data = new Slot
                {
                    Parent = new Reference { TargetID = commonMaterialsRootId },
                    Name = new Field_string { Value = familySlotName },
                },
            },
            CancellationToken.None)).Slot.Value;
        string materialSlotId = (await client.AddSlotAsync(
            new AddSlot
            {
                Data = new Slot
                {
                    Parent = new Reference { TargetID = familySlotId },
                    Name = new Field_string { Value = materialSlotName },
                },
            },
            CancellationToken.None)).Slot.Value;

        string componentId = (await client.AddComponentAsync(
            new AddComponent
            {
                ContainerSlotId = materialSlotId,
                Data = new Component
                {
                    ComponentType = componentType,
                    Members = new Dictionary<string, Member>(StringComparer.Ordinal),
                },
            },
            CancellationToken.None)).Component.Value;

        return new SeededCommonMaterialComponent(componentId, materialSlotId);
    }

    private sealed record SeededCommonMaterialComponent(
        string ComponentId,
        string MaterialSlotId);

    private static void AssertNoPlannedReferences(IEnumerable<Member> members)
    {
        foreach (Member member in members)
        {
            switch (member)
            {
                case Reference reference:
                    Assert.False((reference.TargetID ?? string.Empty).StartsWith("plan:", StringComparison.Ordinal));
                    break;
                case SyncList syncList:
                    AssertNoPlannedReferences(syncList.Elements);
                    break;
            }
        }
    }

    private static TerrainTextureOverlay CreateThirdMeshOverlay(
        string meshCode,
        string urlTemplate,
        int zoomLevel = 17,
        int maxTextureSize = 512)
    {
        Assert.True(PlateauMeshCode.TryGetBounds(
            meshCode,
            out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds));
        return new TerrainTextureOverlay(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse(meshCode),
            UrlTemplate: urlTemplate,
            ZoomLevel: zoomLevel,
            GeographicBounds: new GeographicRectangle(
                bounds.SouthLatitude,
                bounds.NorthLatitude,
                bounds.WestLongitude,
                bounds.EastLongitude),
            MaxTextureSize: maxTextureSize);
    }

    private static int RoundUpToPowerOfTwo(int value)
    {
        int rounded = 1;
        while (rounded < value)
        {
            rounded <<= 1;
        }

        return rounded;
    }

    private sealed class FakeTerrainTileHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string[] segments = request.RequestUri?.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                ?? throw new InvalidOperationException("Tile request URI is missing.");
            int tileX = int.Parse(segments[^2], System.Globalization.CultureInfo.InvariantCulture);
            int tileY = int.Parse(Path.GetFileNameWithoutExtension(segments[^1]), System.Globalization.CultureInfo.InvariantCulture);
            using Image<Rgba32> image = new(
                WebMercatorTileMath.TileSizePixels,
                WebMercatorTileMath.TileSizePixels,
                tileX == 0 && tileY == 0 ? new Rgba32(255, 0, 0, 255) : new Rgba32(0, 255, 0, 255));
            MemoryStream stream = new();
            await image.SaveAsPngAsync(stream, cancellationToken);
            stream.Position = 0;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream),
            };
        }
    }
}
