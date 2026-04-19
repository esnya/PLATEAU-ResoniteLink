using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

using System.Reflection;

namespace Plateau.ResoniteLink.Tests.Targets;

[Collection(BundledCompanionTextureIsolationGroup.Name)]
[Trait("Category", "Slow")]
public sealed class ResoniteLiveSceneImportTargetTests
{
    private const string DatasetName = "scene-test";
    private const string MeshCode = "53394525";
    private static readonly ResoniteLocalOrigin LocalOrigin = new(35.6875, 139.69375, 0.0);

    [Fact]
    public async Task BuildAsyncImportsGeneratedDemTerrainTextureWithCanvasTransform()
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
            requestedOverlay => new GeneratedTerrainTexture(
                new ResoniteRawTextureImport(
                    2,
                    2,
                    ResoniteTextureColorProfiles.Srgb,
                    new byte[16],
                    $"terrain-overlay/{requestedOverlay.PackageName}/{requestedOverlay.ZoomLevel}/generated"),
                new ResoniteFloat2(0.5, 0.25),
                new ResoniteFloat2(0.125, 0.375)));
        ResoniteConstructionMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
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
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh("dem-overlay-material"),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "dem-overlay-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    TextureScale: new ResoniteFloat2(0.8, 0.4),
                    TextureOffset: new ResoniteFloat2(0.25, 0.5),
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    TerrainOverlay: overlay),
            ],
            SourceObjectKey: "dem-overlay-source");

        await ResoniteLiveSceneImportTargetTestSupport.BuildSceneAsync(
            metadata,
            [cityObject],
            client,
            terrainTextureGenerator);

        Assert.Equal([overlay], terrainTextureGenerator.RequestedOverlays);
        ResoniteRawTextureImport importedTexture = Assert.Single(client.ImportedRawTextures);
        Assert.Equal("terrain-overlay/dem/17/generated", importedTexture.Identity);
        Component meshRenderer = Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal)
                && string.Equals(client.SlotsById[request.ContainerSlotId].Name?.Value, "DEM Overlay Object", StringComparison.Ordinal))
            .Data;
        SyncList materials = Assert.IsType<SyncList>(meshRenderer.Members["Materials"]);
        SyncList propertyBlocks = Assert.IsType<SyncList>(meshRenderer.Members["MaterialPropertyBlocks"]);
        string sharedMaterialId = Assert.IsType<Reference>(Assert.Single(materials.Elements)).TargetID;
        Component sharedMaterial = Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ID, sharedMaterialId, StringComparison.Ordinal)).Data;
        Component propertyBlock = Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal)).Data;
        string commonMaterialContainerSlotId = Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ID, sharedMaterialId, StringComparison.Ordinal)).ContainerSlotId;

        Assert.Equal("[FrooxEngine]FrooxEngine.PBS_Metallic", sharedMaterial.ComponentType);
        Assert.Contains(
            "PLATEAU Shared Assets/Common Materials/",
            client.SlotPaths[commonMaterialContainerSlotId],
            StringComparison.Ordinal);
        Field_float2 textureScale = Assert.IsType<Field_float2>(sharedMaterial.Members["TextureScale"]);
        Field_float2 textureOffset = Assert.IsType<Field_float2>(sharedMaterial.Members["TextureOffset"]);
        Assert.Equal(0.4f, textureScale.Value.x, 6);
        Assert.Equal(0.1f, textureScale.Value.y, 6);
        Assert.Equal(0.25f, textureOffset.Value.x, 6);
        Assert.Equal(0.5f, textureOffset.Value.y, 6);
        Assert.Equal("[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", propertyBlock.ComponentType);
        string propertyBlockReferenceId = Assert.IsType<Reference>(Assert.Single(propertyBlocks.Elements)).TargetID;
        AddComponent plannedPropertyBlock = Assert.Single(
            client.Batches
                .SelectMany(static operations => operations)
                .OfType<AddComponent>(),
            operation => string.Equals(operation.Data.ID, propertyBlockReferenceId, StringComparison.Ordinal)
                && string.Equals(operation.Data.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal));
        Assert.False(sharedMaterial.Members.ContainsKey("AlbedoTexture"));

        Component overrideTextureComponent = Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal)
                && string.Equals(request.ContainerSlotId, Assert.Single(client.AddedComponents, static component => string.Equals(component.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticMesh", StringComparison.Ordinal)).ContainerSlotId, StringComparison.Ordinal)).Data;
        Assert.Equal("[FrooxEngine]FrooxEngine.StaticTexture2D", overrideTextureComponent.ComponentType);
        string overrideTextureReferenceId = Assert.IsType<Reference>(plannedPropertyBlock.Data.Members["Texture"]).TargetID;
        _ = Assert.Single(
            client.Batches
                .SelectMany(static operations => operations)
                .OfType<AddComponent>(),
            operation => string.Equals(operation.Data.ID, overrideTextureReferenceId, StringComparison.Ordinal)
                && string.Equals(operation.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal));
        Field_Uri textureUrl = Assert.IsType<Field_Uri>(overrideTextureComponent.Members["URL"]);
        Assert.StartsWith("resdb:///texture/", textureUrl.Value.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsyncSendsHeightMapAsHdrRawTextureAndCreatesGridMesh()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneBuilderRecordingClient client = new();
        ResoniteConstructionMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
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

        await ResoniteLiveSceneImportTargetTestSupport.BuildSceneAsync(metadata, [cityObject], client);

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
        Assert.DoesNotContain(
            client.ComponentsById.Values,
            static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildAsyncDisablesColliderForNoCollisionCityObjects()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneBuilderRecordingClient client = new();
        ResoniteConstructionMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
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
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh("wireframe-material"),
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

        await ResoniteLiveSceneImportTargetTestSupport.BuildSceneAsync(metadata, [cityObject], client);

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
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh("terrain-material"),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "terrain-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    TerrainOverlay: overlay),
            ],
            SourceObjectKey: "terrain-overlay-budget");
        ResoniteConstructionCityObject withoutOverlay = withOverlay with
        {
            Materials =
            [
                withOverlay.Materials[0] with { TerrainOverlay = null },
            ],
        };

        long overlayEstimate = InvokeEstimatedWorkingSetBytes(withOverlay);
        long baselineEstimate = InvokeEstimatedWorkingSetBytes(withoutOverlay);

        Assert.True(overlayEstimate > baselineEstimate);
        Assert.True(overlayEstimate - baselineEstimate >= 100L * 1024L * 1024L);
    }

    private static long InvokeEstimatedWorkingSetBytes(ResoniteConstructionCityObject cityObject)
    {
        MethodInfo method = typeof(ResoniteLiveSceneImportTarget)
            .GetMethod("EstimateCityObjectWorkingSetBytes", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("EstimateCityObjectWorkingSetBytes method not found.");
        return (long)(method.Invoke(null, [cityObject])
            ?? throw new InvalidOperationException("EstimateCityObjectWorkingSetBytes returned null."));
    }

    [Fact]
    public async Task BuildAsyncResolvesPlannedIdsBeforeBatchExecution()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneBuilderRecordingClient client = new();
        string sourceFile = $"udx/bldg/{MeshCode}/plateau_{DatasetName}_bldg_{MeshCode}.gml";
        ResoniteConstructionMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
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
                    MaterialKey: "shared-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped,
                    Family: BundledDefaultMaterialFamilies.Road),
                new ResoniteMaterialBinding(
                    MaterialKey: "payload-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(255, 0, 0, "payload/albedo"),
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1]),
            ],
            SourceObjectKey: "tag-source",
            SourceFileRelativePath: sourceFile);

        await ResoniteLiveSceneImportTargetTestSupport.BuildSceneAsync(metadata, [cityObject], client, enableMeshBake: false);

        Assert.All(client.SlotsById.Values, static slot => AssertNoPlannedIds(slot));
        Assert.All(client.ComponentsById.Values, static component => AssertNoPlannedReferences(component.Members.Values));
    }

    [Fact]
    public async Task BuildAsyncPlacesObjectUnderLodHierarchy()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneBuilderRecordingClient client = new();
        string sourceFile = $"udx/bldg/{MeshCode}/plateau_{DatasetName}_bldg_{MeshCode}.gml";
        ResoniteConstructionMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
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
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh("hierarchy-material"),
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

        await ResoniteLiveSceneImportTargetTestSupport.BuildSceneAsync(metadata, [cityObject], client);

        AddComponent meshRendererRequest = Assert.Single(
            client.AddedComponents,
            static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal));
        Slot objectSlot = client.SlotsById[meshRendererRequest.ContainerSlotId];
        Slot lodSlot = client.SlotsById[objectSlot.Parent!.TargetID];

        Assert.Equal("Hierarchy Building", objectSlot.Name?.Value);
        Assert.Equal("LOD2", lodSlot.Name?.Value);
    }

    [Fact]
    public async Task BuildAsyncDoesNotCreateRedundantCompletionMeshCodePlaceholderSlot()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneBuilderRecordingClient client = new();
        string sourceFile = $"udx/bldg/{MeshCode}/plateau_{DatasetName}_bldg_{MeshCode}.gml";
        ResoniteConstructionMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
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
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh("placeholder-material"),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "placeholder-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Wireframe,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: "placeholder-source",
            SourceFileRelativePath: sourceFile);

        await ResoniteLiveSceneImportTargetTestSupport.BuildSceneAsync(metadata, [cityObject], client);

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
    public async Task BuildAsyncPreservesOriginalNameForNonBakedLod1WhenMeshBakeIsDisabled()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneBuilderRecordingClient client = new();
        string sourceFile = $"udx/bldg/{MeshCode}/plateau_{DatasetName}_bldg_{MeshCode}.gml";
        ResoniteConstructionMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
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
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh("non-baked-material"),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "non-baked-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Wireframe,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: "non-baked-source",
            SourceFileRelativePath: sourceFile);

        await ResoniteLiveSceneImportTargetTestSupport.BuildSceneAsync(
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
    public async Task BuildAsyncFailsFastOnOutOfRangeMaterialSubmeshAssignment()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneBuilderRecordingClient client = new();
        ResoniteConstructionMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
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
            SlotKey: "invalid-submesh-range",
            DisplayName: "Invalid Submesh Range",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh("only-submesh"),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "only-submesh",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1]),
            ],
            SourceObjectKey: "invalid-submesh-range");

        ResoniteMeshValidationException exception = await Assert.ThrowsAsync<ResoniteMeshValidationException>(
            () => ResoniteLiveSceneImportTargetTestSupport.BuildSceneAsync(metadata, [cityObject], client, enableMeshBake: false));

        Assert.Contains("targeted missing submesh index 1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("material_bindings=[only-submesh[1]]", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsyncFailsFastOnDuplicateMaterialSubmeshAssignment()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneBuilderRecordingClient client = new();
        ResoniteConstructionMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
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
                    MaterialKey: "first-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
                new ResoniteMaterialBinding(
                    MaterialKey: "second-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0, 1]),
            ],
            SourceObjectKey: "invalid-submesh-duplicate");

        ResoniteMeshValidationException exception = await Assert.ThrowsAsync<ResoniteMeshValidationException>(
            () => ResoniteLiveSceneImportTargetTestSupport.BuildSceneAsync(metadata, [cityObject], client, enableMeshBake: false));

        Assert.Contains("assigned submesh index 0", exception.Message, StringComparison.Ordinal);
        Assert.Contains("materials=2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsyncFailsFastOnUnassignedMeshSubmesh()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneBuilderRecordingClient client = new();
        ResoniteConstructionMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
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
                    MaterialKey: "first-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: "invalid-submesh-unassigned");

        ResoniteMeshValidationException exception = await Assert.ThrowsAsync<ResoniteMeshValidationException>(
            () => ResoniteLiveSceneImportTargetTestSupport.BuildSceneAsync(metadata, [cityObject], client, enableMeshBake: false));

        Assert.Contains("left submesh index 1 without a material assignment", exception.Message, StringComparison.Ordinal);
        Assert.Contains("materials=1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsyncFailsFastOnTriangleMeshWithoutAnySubmesh()
    {
        using TemporaryDirectory datasetDirectory = new();
        using SceneBuilderRecordingClient client = new();
        ResoniteConstructionMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
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
                    MaterialKey: "unused-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: "invalid-empty-submesh");

        ResoniteMeshValidationException exception = await Assert.ThrowsAsync<ResoniteMeshValidationException>(
            () => ResoniteLiveSceneImportTargetTestSupport.BuildSceneAsync(metadata, [cityObject], client, enableMeshBake: false));

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
                new ResoniteMeshSubmesh(0, "first-material", [0, 1, 2]),
                new ResoniteMeshSubmesh(1, "second-material", [3, 4, 5]),
            ]);
    }

    private static void AssertNoPlannedIds(Slot slot)
    {
        Assert.False((slot.ID ?? string.Empty).StartsWith("plan:", StringComparison.Ordinal));
        Assert.False((slot.Parent?.TargetID ?? string.Empty).StartsWith("plan:", StringComparison.Ordinal));
        Assert.False((slot.Tag?.Value ?? string.Empty).StartsWith("plan:", StringComparison.Ordinal));
    }

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
}
