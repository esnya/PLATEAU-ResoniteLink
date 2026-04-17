using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Targets;

[Collection(BundledCompanionTextureIsolationGroup.Name)]
[Trait("Category", "Slow")]
public sealed class ResoniteLinkSceneBuilderAssetReuseTests
{
    private const string DatasetName = "reuse-test";
    private const string MeshCode = "53394525";
    private const string SecondaryMeshCode = "53394526";
    private const string PrimarySourceFile = $"udx/bldg/{MeshCode}/plateau_{DatasetName}_bldg_{MeshCode}.gml";
    private const string SecondarySourceFile = $"udx/bldg/{SecondaryMeshCode}/plateau_{DatasetName}_bldg_{SecondaryMeshCode}.gml";
    private const string PrimaryDemSourceFile = $"udx/dem/{MeshCode}/plateau_{DatasetName}_dem_{MeshCode}.gml";
    private const string SecondaryDemSourceFile = $"udx/dem/{SecondaryMeshCode}/plateau_{DatasetName}_dem_{SecondaryMeshCode}.gml";
    private static readonly ResoniteLocalOrigin LocalOrigin = new(35.6875, 139.69375, 0.0);

    [Fact]
    public async Task BuildAsyncSharesCommonMaterialAssetsAcrossCityObjectsInSameSession()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneBuilderRecordingClient client = new();

        await ResoniteLinkSceneBuilderTestSupport.BuildSceneAsync(
            metadata,
            [
                CreateBundledTriangleCityObject("shared-material-one"),
                CreateBundledTriangleCityObject("shared-material-two"),
            ],
            client);

        string firstMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject shared-material-one");
        string secondMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject shared-material-two");
        string commonMaterialContainerSlotId = Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ID, firstMaterialId, StringComparison.Ordinal)).ContainerSlotId;

        Assert.Equal(firstMaterialId, secondMaterialId);
        Assert.StartsWith(
            $"PLATEAU {DatasetName}/Assets/Common/",
            client.SlotPaths[commonMaterialContainerSlotId],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsyncDoesNotShareCommonMaterialAssetsWhenUvScaleDiffers()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneBuilderRecordingClient client = new();

        await ResoniteLinkSceneBuilderTestSupport.BuildSceneAsync(
            metadata,
            [
                CreateBundledTriangleCityObject("shared-material-scale-one"),
                CreateBundledTriangleCityObject("shared-material-scale-two", textureScale: new ResoniteFloat2(0.5, 0.5)),
            ],
            client);

        Assert.NotEqual(
            GetRendererMaterialReferenceTarget(client, "CityObject shared-material-scale-one"),
            GetRendererMaterialReferenceTarget(client, "CityObject shared-material-scale-two"));
    }

    [Fact]
    public async Task BuildAsyncDemotesPayloadTexturesToDedicatedMaterials()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneBuilderRecordingClient client = new();

        await ResoniteLinkSceneBuilderTestSupport.BuildSceneAsync(
            metadata,
            [
                CreatePayloadTriangleCityObject(
                    "dataset-texture-one",
                    ResoniteLinkSceneBuilderTestSupport.CreateSolidColorPayload(255, 0, 0, "textures/albedo-one.png")),
                CreatePayloadTriangleCityObject(
                    "dataset-texture-two",
                    ResoniteLinkSceneBuilderTestSupport.CreateSolidColorPayload(0, 255, 0, "textures/albedo-two.png")),
            ],
            client);

        string firstMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject dataset-texture-one");
        string secondMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject dataset-texture-two");
        HashSet<string> commonMaterialIds = client.AddedComponents
            .Where(request => client.SlotPaths.TryGetValue(request.ContainerSlotId, out string? path)
                && path.Contains("/Assets/Common/", StringComparison.Ordinal))
            .Select(static request => request.Data.ID)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEqual(firstMaterialId, secondMaterialId);
        Assert.DoesNotContain(firstMaterialId, commonMaterialIds);
        Assert.DoesNotContain(secondMaterialId, commonMaterialIds);
        Assert.Contains(client.ImportedRawTextures, static texture => texture.Identity == "textures/albedo-one.png");
        Assert.Contains(client.ImportedRawTextures, static texture => texture.Identity == "textures/albedo-two.png");
    }

    [Fact]
    public async Task BuildAsyncPreservesMixedCommonAndDedicatedMaterialOrder()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneBuilderRecordingClient client = new();

        await ResoniteLinkSceneBuilderTestSupport.BuildSceneAsync(
            metadata,
            [
                CreateMixedMaterialCityObject(
                    "mixed-material-order",
                    ResoniteLinkSceneBuilderTestSupport.CreateSolidColorPayload(255, 0, 0, "textures/mixed-albedo.png")),
            ],
            client);

        string[] materialIds = GetRendererMaterialReferenceTargets(client, "CityObject mixed-material-order");
        Assert.Equal(2, materialIds.Length);
        HashSet<string> commonMaterialIds = client.AddedComponents
            .Where(request => client.SlotPaths.TryGetValue(request.ContainerSlotId, out string? path)
                && path.Contains("/Assets/Common/", StringComparison.Ordinal))
            .Select(static request => request.Data.ID)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEqual(materialIds[0], materialIds[1]);
        Assert.Contains(materialIds[0], commonMaterialIds);
        Assert.DoesNotContain(materialIds[1], commonMaterialIds);
        Assert.Contains(client.ImportedRawTextures, static texture => texture.Identity == "textures/mixed-albedo.png");
    }

    [Fact]
    public async Task BuildAsyncReusesNamedDatasetRootAssetsAndCommonAcrossRuns()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneBuilderRecordingClient client = new();

        await ResoniteLinkSceneBuilderTestSupport.BuildSceneTwiceAsync(
            metadata,
            [CreateBundledTriangleCityObject("reuse-run-one")],
            [CreateBundledTriangleCityObject("reuse-run-two")],
            client);

        Slot datasetRoot = ResoniteLinkSceneBuilderTestSupport.FindUniqueSlotByNameOutsideAssets(client, $"PLATEAU {DatasetName}");
        Slot assetsRoot = ResoniteLinkSceneBuilderTestSupport.FindUniqueSlotByPathSuffix(client, $"PLATEAU {DatasetName}/Assets");
        Slot commonRoot = ResoniteLinkSceneBuilderTestSupport.FindUniqueSlotByPathSuffix(client, $"PLATEAU {DatasetName}/Assets/Common");

        Assert.Equal(1, client.SlotsById.Values.Count(slot => string.Equals(slot.Name?.Value, $"PLATEAU {DatasetName}", StringComparison.Ordinal)));
        Assert.Equal(1, client.SlotsById.Values.Count(slot => string.Equals(slot.Name?.Value, "Assets", StringComparison.Ordinal)
            && string.Equals(slot.Parent?.TargetID, datasetRoot.ID, StringComparison.Ordinal)));
        Assert.Equal(1, client.SlotsById.Values.Count(slot => string.Equals(slot.Name?.Value, "Common", StringComparison.Ordinal)
            && string.Equals(slot.Parent?.TargetID, assetsRoot.ID, StringComparison.Ordinal)));
        Assert.True(ResoniteLinkSceneBuilderTestSupport.IsDescendantOf(client, commonRoot.ID, assetsRoot.ID));
        Assert.True(client.ImportedMeshes.Count >= 2);
    }

    [Fact]
    public async Task BuildAsyncAssignsSourceFileRootPositionForNonCompletionMeshAndPreservesWorldPosition()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path, [PrimarySourceFile, SecondarySourceFile]);
        using SceneBuilderRecordingClient client = new();
        ResoniteFloat3 worldPosition = new(123.0, 0.0, 456.0);

        await ResoniteLinkSceneBuilderTestSupport.BuildSceneAsync(
            metadata,
            [
                CreateBundledTriangleCityObject(
                    "offset-run-one",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondarySourceFile,
                    worldPosition: worldPosition),
            ],
            client);

        Slot sourceFileRoot = ResoniteLinkSceneBuilderTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(SecondarySourceFile)}");

        Assert.Equal(0.0, GetSlotPosition(sourceFileRoot).X, 3);
        Assert.Equal(0.0, GetSlotPosition(sourceFileRoot).Z, 3);

        Slot objectSlot = ResoniteLinkSceneBuilderTestSupport.FindUniqueSlotByNameOutsideAssets(client, "CityObject offset-run-one");
        ResoniteFloat3 accumulatedPosition = GetAccumulatedPosition(client, objectSlot);
        Assert.Equal(worldPosition.X, accumulatedPosition.X, 3);
        Assert.Equal(worldPosition.Y, accumulatedPosition.Y, 3);
        Assert.Equal(worldPosition.Z, accumulatedPosition.Z, 3);
    }

    [Fact]
    public async Task BuildAsyncReusesPositionedSourceFileRootAcrossRuns()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path, [PrimarySourceFile, SecondarySourceFile]);
        using SceneBuilderRecordingClient client = new();
        ResoniteFloat3 secondRunWorldPosition = new(200.0, 0.0, 300.0);

        await ResoniteLinkSceneBuilderTestSupport.BuildSceneTwiceAsync(
            metadata,
            [
                CreateBundledTriangleCityObject(
                    "offset-run-one",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondarySourceFile,
                    worldPosition: new ResoniteFloat3(123.0, 0.0, 456.0)),
            ],
            [
                CreateBundledTriangleCityObject(
                    "offset-run-two",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondarySourceFile,
                    worldPosition: secondRunWorldPosition),
            ],
            client);

        string sourceFileRootName = Path.GetFileNameWithoutExtension(SecondarySourceFile);
        Slot sourceFileRoot = ResoniteLinkSceneBuilderTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{sourceFileRootName}");

        Assert.Equal(
            1,
            client.SlotsById.Values.Count(slot => string.Equals(slot.Name?.Value, sourceFileRootName, StringComparison.Ordinal)
                && string.Equals(slot.Parent?.TargetID, sourceFileRoot.Parent?.TargetID, StringComparison.Ordinal)));

        Slot objectSlot = ResoniteLinkSceneBuilderTestSupport.FindUniqueSlotByNameOutsideAssets(client, "CityObject offset-run-two");
        ResoniteFloat3 accumulatedPosition = GetAccumulatedPosition(client, objectSlot);
        AssertNear(secondRunWorldPosition, accumulatedPosition, 0.2);
    }

    [Fact]
    public async Task BuildAsyncAssignsSourceFileRootPositionForHeightMapDemAndPreservesWorldPosition()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateDemMetadata(datasetDirectory.Path, [PrimaryDemSourceFile, SecondaryDemSourceFile]);
        using SceneBuilderRecordingClient client = new();
        ResoniteFloat3 worldPosition = new(123.0, 15.5, 456.0);

        await ResoniteLinkSceneBuilderTestSupport.BuildSceneAsync(
            metadata,
            [
                CreateHeightMapDemCityObject(
                    "dem-heightmap-run-one",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondaryDemSourceFile,
                    worldPosition: worldPosition),
            ],
            client);

        Slot sourceFileRoot = ResoniteLinkSceneBuilderTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(SecondaryDemSourceFile)}");

        Assert.Equal(0.0, GetSlotPosition(sourceFileRoot).X, 3);
        Assert.Equal(0.0, GetSlotPosition(sourceFileRoot).Z, 3);

        Slot objectSlot = ResoniteLinkSceneBuilderTestSupport.FindUniqueSlotByNameOutsideAssets(client, "DEM HeightMap dem-heightmap-run-one");
        ResoniteFloat3 accumulatedPosition = GetAccumulatedPosition(client, objectSlot);
        Assert.Equal(worldPosition.X, accumulatedPosition.X, 3);
        Assert.Equal(worldPosition.Y, accumulatedPosition.Y, 3);
        Assert.Equal(worldPosition.Z, accumulatedPosition.Z, 3);
    }

    [Fact]
    public async Task BuildAsyncReusesPositionedSourceFileRootAcrossRunsForHeightMapDem()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateDemMetadata(datasetDirectory.Path, [PrimaryDemSourceFile, SecondaryDemSourceFile]);
        using SceneBuilderRecordingClient client = new();
        ResoniteFloat3 secondRunWorldPosition = new(200.0, 25.0, 300.0);

        await ResoniteLinkSceneBuilderTestSupport.BuildSceneTwiceAsync(
            metadata,
            [
                CreateHeightMapDemCityObject(
                    "dem-heightmap-run-one",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondaryDemSourceFile,
                    worldPosition: new ResoniteFloat3(123.0, 15.5, 456.0)),
            ],
            [
                CreateHeightMapDemCityObject(
                    "dem-heightmap-run-two",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondaryDemSourceFile,
                    worldPosition: secondRunWorldPosition),
            ],
            client);

        string sourceFileRootName = Path.GetFileNameWithoutExtension(SecondaryDemSourceFile);
        Slot sourceFileRoot = ResoniteLinkSceneBuilderTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{sourceFileRootName}");

        Assert.Equal(
            1,
            client.SlotsById.Values.Count(slot => string.Equals(slot.Name?.Value, sourceFileRootName, StringComparison.Ordinal)
                && string.Equals(slot.Parent?.TargetID, sourceFileRoot.Parent?.TargetID, StringComparison.Ordinal)));

        Slot objectSlot = ResoniteLinkSceneBuilderTestSupport.FindUniqueSlotByNameOutsideAssets(client, "DEM HeightMap dem-heightmap-run-two");
        ResoniteFloat3 accumulatedPosition = GetAccumulatedPosition(client, objectSlot);
        AssertNear(secondRunWorldPosition, accumulatedPosition, 0.2);
    }

    [Fact]
    public async Task CompleteAsyncReturnsLocationAnchoredToResolvedSourceFileRoot()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path, [PrimarySourceFile, SecondarySourceFile]);
        using SceneBuilderRecordingClient client = new();
        using TemporaryDirectory workDirectory = new();
        await using ResoniteLinkSceneBuilder builder = ResoniteLinkSceneBuilderTestSupport.CreateBuilder(client);

        await builder.BeginAsync(metadata, workDirectory.Path);
        await builder.ProcessCityObjectAsync(
            CreateBundledTriangleCityObject(
                "completion-root",
                actualMeshCode: MeshCode,
                sourceFileRelativePath: PrimarySourceFile,
                worldPosition: new ResoniteFloat3(123.0, 0.0, 456.0)));

        IReadOnlyList<string> destinations = await builder.CompleteAsync();

        Slot sourceFileRoot = ResoniteLinkSceneBuilderTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(PrimarySourceFile)}");
        Assert.Equal(
            $"ws://localhost:12345/#{sourceFileRoot.ID}",
            Assert.Single(destinations));
    }

    [Fact]
    public async Task BuildAsyncAppendsIntoAssetsOnlyDatasetRootAndAnchorsFirstActualSourceFileRootAtDatasetRoot()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path, [PrimarySourceFile, SecondarySourceFile]);
        using SceneBuilderRecordingClient client = new();
        using TemporaryDirectory firstWorkDirectory = new();
        using TemporaryDirectory secondWorkDirectory = new();

        await using (ResoniteLinkSceneBuilder builder = ResoniteLinkSceneBuilderTestSupport.CreateBuilder(client))
        {
            await builder.BeginAsync(metadata, firstWorkDirectory.Path);
            _ = await builder.CompleteAsync();
        }

        await using (ResoniteLinkSceneBuilder builder = ResoniteLinkSceneBuilderTestSupport.CreateBuilder(client))
        {
            await builder.BeginAsync(metadata, secondWorkDirectory.Path);
            await builder.ProcessCityObjectAsync(
                CreateBundledTriangleCityObject(
                    "assets-only-append",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondarySourceFile,
                    worldPosition: new ResoniteFloat3(10.0, 0.0, 20.0)));

            IReadOnlyList<string> destinations = await builder.CompleteAsync();
            Slot sourceFileRoot = ResoniteLinkSceneBuilderTestSupport.FindUniqueSlotByPathSuffix(
                client,
                $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(SecondarySourceFile)}");

            Assert.Equal(0.0, GetSlotPosition(sourceFileRoot).X, 3);
            Assert.Equal(0.0, GetSlotPosition(sourceFileRoot).Z, 3);
            Assert.Equal($"ws://localhost:12345/#{sourceFileRoot.ID}", Assert.Single(destinations));
        }
    }

    private static ResoniteConstructionMetadata CreateMetadata(string datasetRoot, IReadOnlyList<string>? sourceFiles = null)
    {
        return ResoniteLinkSceneBuilderTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetRoot,
            LocalOrigin,
            packageNames: ["bldg"],
            sourceFiles: sourceFiles ?? [PrimarySourceFile]);
    }

    private static ResoniteConstructionMetadata CreateDemMetadata(string datasetRoot, IReadOnlyList<string>? sourceFiles = null)
    {
        return ResoniteLinkSceneBuilderTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetRoot,
            LocalOrigin,
            packageNames: ["dem"],
            sourceFiles: sourceFiles ?? [PrimaryDemSourceFile]);
    }

    private static ResoniteConstructionCityObject CreateBundledTriangleCityObject(
        string objectIdentity,
        ResoniteFloat2? textureScale = null,
        string actualMeshCode = MeshCode,
        string sourceFileRelativePath = PrimarySourceFile,
        ResoniteFloat3? worldPosition = null)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "bldg",
            ActualMeshCode: actualMeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(worldPosition ?? new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLinkSceneBuilderTestSupport.CreateTriangleMesh("triangle-material"),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "triangle-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    TextureScale: textureScale,
                    Family: BundledDefaultMaterialFamilies.Facade,
                    AssetScope: ResoniteMaterialAssetScope.Common,
                    BundledVariantIndex: 0),
            ],
            SourceObjectKey: objectIdentity,
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private static ResoniteConstructionCityObject CreatePayloadTriangleCityObject(
        string objectIdentity,
        ResoniteTexturePayload payload,
        string sourceFileRelativePath = PrimarySourceFile)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLinkSceneBuilderTestSupport.CreateTriangleMesh("triangle-material"),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "triangle-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: payload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    Family: BundledDefaultMaterialFamilies.Facade,
                    AssetScope: ResoniteMaterialAssetScope.Common),
            ],
            SourceObjectKey: objectIdentity,
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private static ResoniteConstructionCityObject CreateHeightMapDemCityObject(
        string objectIdentity,
        string actualMeshCode = MeshCode,
        string sourceFileRelativePath = PrimaryDemSourceFile,
        ResoniteFloat3? worldPosition = null)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"DEM HeightMap {objectIdentity}",
            PackageName: "dem",
            ActualMeshCode: actualMeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(worldPosition ?? new ResoniteFloat3(0.0, 0.0, 0.0)),
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
                    MaterialKey: "dem-heightmap-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Wireframe,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: objectIdentity,
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private static ResoniteConstructionCityObject CreateMixedMaterialCityObject(
        string objectIdentity,
        ResoniteTexturePayload payload,
        string sourceFileRelativePath = PrimarySourceFile)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: CreateTwoSubmeshMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "mixed-common-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    Family: BundledDefaultMaterialFamilies.Facade,
                    AssetScope: ResoniteMaterialAssetScope.Common),
                new ResoniteMaterialBinding(
                    MaterialKey: "mixed-dedicated-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: payload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    Family: BundledDefaultMaterialFamilies.Facade,
                    AssetScope: ResoniteMaterialAssetScope.Common),
            ],
            SourceObjectKey: objectIdentity,
            SourceFileRelativePath: sourceFileRelativePath);
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
                new ResoniteMeshSubmesh(0, "mixed-common-material", [0, 1, 2]),
                new ResoniteMeshSubmesh(1, "mixed-dedicated-material", [3, 4, 5]),
            ]);
    }

    private static ResoniteFloat3 GetSlotPosition(Slot slot)
    {
        return slot.Position is Field_float3 position
            ? new ResoniteFloat3(position.Value.x, position.Value.y, position.Value.z)
            : new ResoniteFloat3(0.0, 0.0, 0.0);
    }

    private static ResoniteFloat3 GetAccumulatedPosition(SceneBuilderRecordingClient client, Slot slot)
    {
        ResoniteFloat3 accumulated = new(0.0, 0.0, 0.0);
        Slot? current = slot;
        while (current is not null)
        {
            accumulated = Add(accumulated, GetSlotPosition(current));
            if (current.Parent is null
                || string.IsNullOrWhiteSpace(current.Parent.TargetID)
                || !client.SlotsById.TryGetValue(current.Parent.TargetID, out current))
            {
                break;
            }
        }

        return accumulated;
    }

    private static ResoniteFloat3 Add(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    private static void AssertNear(ResoniteFloat3 expected, ResoniteFloat3 actual, double tolerance)
    {
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
        Assert.InRange(actual.Z, expected.Z - tolerance, expected.Z + tolerance);
    }

    private static string GetRendererMaterialReferenceTarget(
        SceneBuilderRecordingClient client,
        string slotName)
    {
        Component renderer = Assert.Single(
            client.AddedComponents.Where(request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal)
                    && string.Equals(client.SlotsById[request.ContainerSlotId].Name?.Value, slotName, StringComparison.Ordinal))
                .Select(static request => request.Data));
        SyncList materials = Assert.IsType<SyncList>(renderer.Members["Materials"]);
        Reference materialReference = Assert.IsType<Reference>(Assert.Single(materials.Elements));
        return materialReference.TargetID;
    }

    private static string[] GetRendererMaterialReferenceTargets(
        SceneBuilderRecordingClient client,
        string slotName)
    {
        Component renderer = Assert.Single(
            client.AddedComponents.Where(request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal)
                    && string.Equals(client.SlotsById[request.ContainerSlotId].Name?.Value, slotName, StringComparison.Ordinal))
                .Select(static request => request.Data));
        SyncList materials = Assert.IsType<SyncList>(renderer.Members["Materials"]);
        return materials.Elements
            .Select(Assert.IsType<Reference>)
            .Select(static reference => reference.TargetID)
            .ToArray();
    }

}
