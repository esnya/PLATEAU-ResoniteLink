using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Plateau.ResoniteLink.Tests.Cli;

[Collection(BundledCompanionTextureIsolationGroup.Name)]
public sealed class ResoniteLinkSceneBuilderAssetReuseTests
{
    private const string DatasetName = "reuse-test";
    private const string MeshCode = "53394525";
    [Fact]
    public async Task BuildAsyncImportsDedicatedTriangleMeshesForDistinctCityObjectsInSameRun()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using ReuseSessionSharedClient sharedClient = new();

        CapturedScene scene = new(
            metadata,
            [
                CreateTriangleCityObject(
                    objectIdentity: "triangle-one",
                    mesh: CreateTriangleMesh(0.0, 1.0, 2.0, "triangle-textured-material")),
                CreateTriangleCityObject(
                    objectIdentity: "triangle-two",
                    mesh: CreateTriangleMesh(3.0, 4.0, 5.0, "triangle-material")),
            ]);

        await BuildSceneOnceAsync(scene, sharedClient, Path.Combine(datasetDirectory.Path, "work"));

        Assert.Equal(scene.CityObjects.Count, sharedClient.ImportedMeshes.Count);
    }

    [Fact]
    public async Task BuildAsyncSharesRegularTextureImportsAcrossCityObjectsInSameRun()
    {
        using TemporaryDirectory datasetDirectory = new();
        string texturePath = "textures/albedo.png";
        Directory.CreateDirectory(Path.Combine(datasetDirectory.Path, "textures"));
        await WriteSolidColorTextureAsync(
            Path.Combine(datasetDirectory.Path, texturePath),
            new Rgba32(255, 0, 0, 255));

        ResoniteConstructionMetadata metadata = CreateMetadata(
            datasetDirectory.Path,
            sourceFiles: [texturePath]);
        using ReuseSessionSharedClient sharedClient = new();

        CapturedScene scene = new(
            metadata,
            [
                CreateTexturedTriangleCityObject(
                    objectIdentity: "shared-regular-texture-one",
                    texturePath,
                    mesh: CreateTriangleMesh(0.0, 1.0, 2.0, "triangle-textured-material")),
                CreateTexturedTriangleCityObject(
                    objectIdentity: "shared-regular-texture-two",
                    texturePath,
                    mesh: CreateTriangleMesh(2.0, 3.0, 4.0, "triangle-textured-material")),
            ]);

        await BuildSceneOnceAsync(scene, sharedClient, Path.Combine(datasetDirectory.Path, "work"));

        string importedTexturePath = Assert.Single(sharedClient.ImportedTexturePaths);
        Assert.Equal(Path.Combine(datasetDirectory.Path, texturePath), importedTexturePath);
    }

    [Fact]
    public async Task BuildAsyncImportsSeparateHeightmapTexturesForDistinctHeightmapObjectsInSameRun()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path, packageNames: ["terrain"]);
        using ReuseSessionSharedClient sharedClient = new();

        CapturedScene scene = new(
            metadata,
            [
                CreateHeightMapCityObject(
                    objectIdentity: "shared-heightmap-one",
                    heightSamples: [0, 1, 2, 3]),
                CreateHeightMapCityObject(
                    objectIdentity: "shared-heightmap-two",
                    heightSamples: [3, 2, 1, 0]),
            ]);

        await BuildSceneOnceAsync(scene, sharedClient, Path.Combine(datasetDirectory.Path, "work"));

        Assert.Equal(scene.CityObjects.Count, sharedClient.ImportedRawHdrTextures.Count);
    }

    [Fact]
    public async Task BuildAsyncSharesBundledCompanionTextureImportsAcrossCityObjectsInSameRun()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path, packageNames: ["bldg"]);
        using ReuseSessionSharedClient sharedClient = new();

        string bundledTexturePath = BundledDefaultMaterialFamilies.FacadeVariants[0];
        CapturedScene scene = new(
            metadata,
            [
                CreateBundledTriangleCityObject(
                    objectIdentity: "shared-bundled-companion-one",
                    texturePath: bundledTexturePath,
                    mesh: CreateTriangleMesh(0.0, 1.0, 2.0, "triangle-textured-material")),
                CreateBundledTriangleCityObject(
                    objectIdentity: "shared-bundled-companion-two",
                    texturePath: bundledTexturePath,
                    mesh: CreateTriangleMesh(3.0, 4.0, 5.0, "triangle-textured-material")),
            ]);

        await BuildSceneOnceAsync(scene, sharedClient, Path.Combine(datasetDirectory.Path, "work"));

        Assert.InRange(sharedClient.ImportedTexturePaths.Count, 4, 5);
    }

    [Fact]
    public async Task BuildAsyncSharesCommonMaterialAssetsAcrossCityObjectsInSameSession()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path, packageNames: ["bldg"]);
        using ReuseSessionSharedClient sharedClient = new();

        string bundledTexturePath = BundledDefaultMaterialFamilies.FacadeVariants[0];
        CapturedScene scene = new(
            metadata,
            [
                CreateBundledTriangleCityObject(
                    objectIdentity: "shared-material-one",
                    texturePath: bundledTexturePath,
                    mesh: CreateTriangleMesh(0.0, 1.0, 2.0, "triangle-textured-material")),
                CreateBundledTriangleCityObject(
                    objectIdentity: "shared-material-two",
                    texturePath: bundledTexturePath,
                    mesh: CreateTriangleMesh(3.0, 4.0, 5.0, "triangle-textured-material")),
            ]);

        await BuildSceneOnceAsync(scene, sharedClient, Path.Combine(datasetDirectory.Path, "work"));

        Assert.Equal(
            1,
            sharedClient.AddedComponents.Count(static request =>
                string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal)));
        Assert.InRange(sharedClient.ImportedTexturePaths.Count, 4, 5);
    }

    [Fact]
    public async Task BuildAsyncDoesNotShareCommonMaterialAssetsWhenUvScaleDiffers()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path, packageNames: ["bldg"]);
        using ReuseSessionSharedClient sharedClient = new();

        string bundledTexturePath = BundledDefaultMaterialFamilies.FacadeVariants[0];
        CapturedScene scene = new(
            metadata,
            [
                CreateBundledTriangleCityObject(
                    objectIdentity: "shared-material-scale-one",
                    texturePath: bundledTexturePath,
                    mesh: CreateTriangleMesh(0.0, 1.0, 2.0, "triangle-textured-material"),
                    textureScale: new ResoniteFloat2(1.0, 1.0)),
                CreateBundledTriangleCityObject(
                    objectIdentity: "shared-material-scale-two",
                    texturePath: bundledTexturePath,
                    mesh: CreateTriangleMesh(3.0, 4.0, 5.0, "triangle-textured-material"),
                    textureScale: new ResoniteFloat2(0.5, 0.5)),
            ]);

        await BuildSceneOnceAsync(scene, sharedClient, Path.Combine(datasetDirectory.Path, "work"));

        Assert.Equal(
            2,
            sharedClient.AddedComponents.Count(static request =>
                string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task BuildAsyncDoesNotShareCommonMaterialAssetsWhenDepthOffsetDiffers()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path, packageNames: ["tran", "frn"]);
        using ReuseSessionSharedClient sharedClient = new();

        string bundledTexturePath = BundledDefaultMaterialFamilies.CityFurnitureVariants[0];
        CapturedScene scene = new(
            metadata,
            [
                CreateBundledTriangleCityObject(
                    objectIdentity: "shared-material-depth-one",
                    texturePath: bundledTexturePath,
                    mesh: CreateTriangleMesh(0.0, 1.0, 2.0, "triangle-textured-material"),
                    depthOffset: LocalCityGmlResonitePlanBuilder.DefaultTerrainSurfaceDepthOffset),
                CreateBundledTriangleCityObject(
                    objectIdentity: "shared-material-depth-two",
                    texturePath: bundledTexturePath,
                    mesh: CreateTriangleMesh(3.0, 4.0, 5.0, "triangle-textured-material"),
                    depthOffset: LocalCityGmlResonitePlanBuilder.DefaultTerrainFineOverlayDepthOffset),
            ]);

        await BuildSceneOnceAsync(scene, sharedClient, Path.Combine(datasetDirectory.Path, "work"));

        Assert.Equal(
            2,
            sharedClient.AddedComponents.Count(static request =>
                string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task BuildAsyncSharesAlbedoOnlyCommonMaterialWhenTextureBasenamesMatchButPathsDiffer()
    {
        using TemporaryDirectory datasetDirectory = new();
        string firstTexturePath = "textures/set-a/wall.png";
        string secondTexturePath = "textures/set-b/wall.png";
        await WriteSolidColorTextureAsync(
            Path.Combine(datasetDirectory.Path, firstTexturePath),
            new Rgba32(255, 0, 0, 255));
        await WriteSolidColorTextureAsync(
            Path.Combine(datasetDirectory.Path, secondTexturePath),
            new Rgba32(0, 255, 0, 255));

        ResoniteConstructionMetadata metadata = CreateMetadata(
            datasetDirectory.Path,
            packageNames: ["bldg"],
            sourceFiles: [firstTexturePath, secondTexturePath]);
        using ReuseSessionSharedClient sharedClient = new();

        CapturedScene scene = new(
            metadata,
            [
                CreateMultiTexturedTriangleCityObject(
                    objectIdentity: "shared-material-path",
                    firstTexturePath,
                    secondTexturePath),
            ]);

        await BuildSceneOnceAsync(scene, sharedClient, Path.Combine(datasetDirectory.Path, "work"));

        Assert.Equal(
            1,
            sharedClient.AddedComponents.Count(static request =>
                string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal)));
        Assert.Equal(
            2,
            sharedClient.AddedComponents.Count(static request =>
                string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task BuildAsyncSharesAlbedoOnlyCommonMaterialWhenTexturePathsDifferOnlyByColorSuffix()
    {
        using TemporaryDirectory datasetDirectory = new();
        string firstTexturePath = "textures/facade.png";
        string secondTexturePath = "textures/facade_Color.png";
        await WriteSolidColorTextureAsync(
            Path.Combine(datasetDirectory.Path, firstTexturePath),
            new Rgba32(255, 0, 0, 255));
        await WriteSolidColorTextureAsync(
            Path.Combine(datasetDirectory.Path, secondTexturePath),
            new Rgba32(0, 255, 0, 255));

        ResoniteConstructionMetadata metadata = CreateMetadata(
            datasetDirectory.Path,
            packageNames: ["bldg"],
            sourceFiles: [firstTexturePath, secondTexturePath]);
        using ReuseSessionSharedClient sharedClient = new();

        CapturedScene scene = new(
            metadata,
            [
                CreateMultiTexturedTriangleCityObject(
                    objectIdentity: "shared-material-color-suffix",
                    firstTexturePath,
                    secondTexturePath),
            ]);

        await BuildSceneOnceAsync(scene, sharedClient, Path.Combine(datasetDirectory.Path, "work"));

        Assert.Equal(
            1,
            sharedClient.AddedComponents.Count(static request =>
                string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal)));
        Assert.Equal(
            2,
            sharedClient.AddedComponents.Count(static request =>
                string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task BuildAsyncUsesSharedCommonMaterialForDatasetAlbedoOnlyTextures()
    {
        using TemporaryDirectory datasetDirectory = new();
        string textureOne = "textures/albedo-one.png";
        string textureTwo = "textures/albedo-two.png";
        Directory.CreateDirectory(Path.Combine(datasetDirectory.Path, "textures"));
        await WriteSolidColorTextureAsync(
            Path.Combine(datasetDirectory.Path, textureOne),
            new Rgba32(255, 0, 0, 255));
        await WriteSolidColorTextureAsync(
            Path.Combine(datasetDirectory.Path, textureTwo),
            new Rgba32(0, 255, 0, 255));

        ResoniteConstructionMetadata metadata = CreateMetadata(
            datasetDirectory.Path,
            sourceFiles: [textureOne, textureTwo]);
        using ReuseSessionSharedClient sharedClient = new();
        CapturedScene scene = new(
            metadata,
            [
                CreateTexturedTriangleCityObject(
                    objectIdentity: "dataset-albedo-one",
                    textureOne,
                    mesh: CreateTriangleMesh(0.0, 1.0, 2.0, "triangle-textured-material")),
                CreateTexturedTriangleCityObject(
                    objectIdentity: "dataset-albedo-two",
                    textureTwo,
                    mesh: CreateTriangleMesh(3.0, 4.0, 5.0, "triangle-textured-material")),
            ]);

        await BuildSceneOnceAsync(scene, sharedClient, Path.Combine(datasetDirectory.Path, "work"));

        Assert.Equal(
            1,
            sharedClient.AddedComponents.Count(static request =>
                string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal)));
        Assert.Equal(
            2,
            sharedClient.AddedComponents.Count(static request =>
                string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task BuildAsyncReusesSharedCommonMaterialForDatasetAlbedoOnlyTexturesAcrossRuns()
    {
        using TemporaryDirectory datasetDirectory = new();
        string firstTexturePath = "textures/albedo-one.png";
        string secondTexturePath = "textures/albedo-two.png";
        Directory.CreateDirectory(Path.Combine(datasetDirectory.Path, "textures"));
        await WriteSolidColorTextureAsync(
            Path.Combine(datasetDirectory.Path, firstTexturePath),
            new Rgba32(255, 0, 0, 255));
        await WriteSolidColorTextureAsync(
            Path.Combine(datasetDirectory.Path, secondTexturePath),
            new Rgba32(0, 255, 0, 255));

        ResoniteConstructionMetadata metadata = CreateMetadata(
            datasetDirectory.Path,
            sourceFiles: [firstTexturePath, secondTexturePath]);
        using ReuseSessionSharedClient client = new();

        CapturedScene firstScene = new(
            metadata,
            [
                CreateTexturedTriangleCityObject(
                    objectIdentity: "dataset-albedo-run-one",
                    firstTexturePath,
                    mesh: CreateTriangleMesh(0.0, 1.0, 2.0, "triangle-textured-material")),
            ]);
        CapturedScene secondScene = new(
            metadata,
            [
                CreateTexturedTriangleCityObject(
                    objectIdentity: "dataset-albedo-run-two",
                    secondTexturePath,
                    mesh: CreateTriangleMesh(3.0, 4.0, 5.0, "triangle-textured-material")),
            ]);

        await BuildSceneOnceAsync(firstScene, client, Path.Combine(datasetDirectory.Path, "work-first"));
        await BuildSceneOnceAsync(secondScene, client, Path.Combine(datasetDirectory.Path, "work-second"));

        Assert.Equal(
            1,
            client.AddedComponents.Count(static request =>
                string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal)));
        Assert.Equal(
            2,
            client.AddedComponents.Count(static request =>
                string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task BuildAsyncPlacesMaterialAndTextureComponentsOnSameCommonAssetSlot()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path, packageNames: ["bldg"]);
        string bundledTexturePath = BundledDefaultMaterialFamilies.FacadeVariants[0];
        CapturedScene scene = new(
            metadata,
            [CreateBundledTriangleCityObject(
                objectIdentity: "same-slot-material-components",
                texturePath: bundledTexturePath,
                mesh: CreateTriangleMesh(0.0, 1.0, 2.0, "triangle-textured-material"))]);
        using ReuseSessionSharedClient client = new();

        await BuildSceneOnceAsync(scene, client, Path.Combine(datasetDirectory.Path, "work"));

        AddComponent materialRequest = Assert.Single(
            client.AddedComponents,
            static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal));
        AddComponent[] textureRequests = client.AddedComponents
            .Where(static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(textureRequests);
        Assert.All(textureRequests, request => Assert.Equal(materialRequest.ContainerSlotId, request.ContainerSlotId));
        Slot materialSlot = client.SlotsById[materialRequest.ContainerSlotId];
        Assert.StartsWith("pbs-uv_", materialSlot.Name?.Value, StringComparison.Ordinal);
        Assert.NotNull(materialSlot.Parent);
        Slot parentSlot = client.SlotsById[materialSlot.Parent!.TargetID];
        Assert.Equal("Common", parentSlot.Name?.Value);
    }

    [Fact]
    public async Task BuildAsyncReusesNamedDatasetRootAndSharedHierarchyAcrossRuns()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path, packageNames: ["bldg"]);
        using ReuseSessionSharedClient client = new();

        CapturedScene firstScene = new(
            metadata,
            [CreateBundledTriangleCityObject(
                objectIdentity: "reuse-run-one",
                texturePath: BundledDefaultMaterialFamilies.FacadeVariants[0],
                mesh: CreateTriangleMesh(0.0, 1.0, 2.0, "triangle-textured-material"))]);
        CapturedScene secondScene = new(
            metadata,
            [CreateBundledTriangleCityObject(
                objectIdentity: "reuse-run-two",
                texturePath: BundledDefaultMaterialFamilies.FacadeVariants[0],
                mesh: CreateTriangleMesh(3.0, 4.0, 5.0, "triangle-textured-material"))]);

        await BuildSceneOnceAsync(firstScene, client, Path.Combine(datasetDirectory.Path, "work-first"));
        await BuildSceneOnceAsync(secondScene, client, Path.Combine(datasetDirectory.Path, "work-second"));

        Slot datasetRoot = Assert.Single(
            client.SlotsById.Values,
            static slot => string.Equals(slot.Name?.Value, "PLATEAU reuse-test", StringComparison.Ordinal));
        Slot assets = AssertSingleChild(datasetRoot, "Assets", client.SlotsById);
        Slot common = AssertSingleChild(assets, "Common", client.SlotsById);
        Slot meshRoot = AssertSingleChild(datasetRoot, MeshCode, client.SlotsById);

        AssertSingleChild(meshRoot, "bldg", client.SlotsById);
        Assert.Contains(
            client.SlotsById.Values,
            slot => string.Equals(slot.Parent?.TargetID, common.ID, StringComparison.Ordinal)
                && slot.Name?.Value is not null
                && slot.Name.Value.StartsWith("pbs-uv_", StringComparison.Ordinal));
        Assert.Equal(1, client.SlotsById.Values.Count(slot => string.Equals(slot.Name?.Value, "PLATEAU reuse-test", StringComparison.Ordinal)));
        Assert.Equal(1, client.SlotsById.Values.Count(slot => string.Equals(slot.Name?.Value, "Assets", StringComparison.Ordinal) && string.Equals(slot.Parent?.TargetID, datasetRoot.ID, StringComparison.Ordinal)));
        Assert.Equal(1, client.SlotsById.Values.Count(slot => string.Equals(slot.Name?.Value, "Common", StringComparison.Ordinal) && string.Equals(slot.Parent?.TargetID, assets.ID, StringComparison.Ordinal)));
        Assert.Equal(1, client.SlotsById.Values.Count(slot => string.Equals(slot.Name?.Value, MeshCode, StringComparison.Ordinal) && string.Equals(slot.Parent?.TargetID, datasetRoot.ID, StringComparison.Ordinal)));
        Assert.Equal(2, client.ImportedMeshes.Count);
    }

    private static async Task BuildSceneOnceAsync(
        CapturedScene scene,
        ReuseSessionSharedClient client,
        string workRoot)
    {
        await using ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => client);

        await builder.BeginAsync(scene.Metadata, workRoot);
        foreach (ResoniteConstructionCityObject cityObject in scene.CityObjects)
        {
            await builder.ProcessCityObjectAsync(cityObject);
        }

        await builder.CompleteAsync();
    }

    private static Slot AssertSingleChild(
        Slot parent,
        string childName,
        IReadOnlyDictionary<string, Slot> slotsById)
    {
        return Assert.Single(
            slotsById.Values,
            slot => string.Equals(slot.Parent?.TargetID, parent.ID, StringComparison.Ordinal)
                && string.Equals(slot.Name?.Value, childName, StringComparison.Ordinal));
    }

    private static async Task WriteSolidColorTextureAsync(string path, Rgba32 color)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using Image<Rgba32> image = new(2, 2, color);
        string extension = Path.GetExtension(path);
        if (string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            await image.SaveAsJpegAsync(path);
            return;
        }

        await image.SaveAsPngAsync(path);
    }

    private static ResoniteConstructionMetadata CreateMetadata(
        string datasetRoot,
        string[]? packageNames = null,
        string[]? sourceFiles = null)
    {
        string[] resolvedPackageNames = packageNames ?? ["bldg"];
        string[] resolvedSourceFiles = sourceFiles ?? [];

        return new ResoniteConstructionMetadata(
            SchemaVersion: "3.0",
            WorldName: $"PLATEAU {DatasetName} {MeshCode}",
            Request: new PlateauImportRequest(
                Dataset: DatasetName,
                MeshCode: MeshCode,
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot,
                ServerUri: null,
                PackageNames: resolvedPackageNames),
            SourceDataset: new PlateauSourceDataset(
                PackageNames: resolvedPackageNames,
                SourceFiles: resolvedSourceFiles,
                TerrainTextureOverlays: []),
            Attribution: new ResoniteAttribution(
                DatasetLicense: new ResoniteLicenseComponentMetadata(
                    RequireCredit: true,
                    CreditText: "Test license",
                    LicenseName: "Test",
                    LicenseUrl: "https://example.com/license"),
                MaterialLicenses: []),
            LocalOrigin: new ResoniteLocalOrigin(35.0, 139.0, 0.0));
    }

    private static ResoniteConstructionCityObject CreateTriangleCityObject(
        string objectIdentity,
        ResoniteImportedMesh mesh)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: mesh,
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "triangle-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: objectIdentity);
    }

    private static ResoniteConstructionCityObject CreateTexturedTriangleCityObject(
        string objectIdentity,
        string texturePath,
        ResoniteImportedMesh mesh)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: mesh,
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "triangle-textured-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: texturePath,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: objectIdentity);
    }

    private static ResoniteConstructionCityObject CreateBundledTriangleCityObject(
        string objectIdentity,
        string texturePath,
        ResoniteImportedMesh mesh,
        ResoniteFloat2? textureScale = null,
        ResoniteColor? baseColor = null,
        ResoniteMaterialDepthOffset? depthOffset = null)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: mesh,
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "triangle-textured-material",
                    BaseColor: baseColor ?? new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: texturePath,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: depthOffset,
                    SubmeshIndices: [0],
                    TextureScale: textureScale),
            ],
            SourceObjectKey: objectIdentity);
    }

    private static ResoniteConstructionCityObject CreateMultiTexturedTriangleCityObject(
        string objectIdentity,
        string firstTexturePath,
        string secondTexturePath)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: new ResoniteImportedMesh(
                [
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 1.0)),
                ],
                [
                    new ResoniteMeshSubmesh(0, "mat-one", [0, 1, 2]),
                    new ResoniteMeshSubmesh(1, "mat-two", [1, 3, 2]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "mat-one",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: firstTexturePath,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
                new ResoniteMaterialBinding(
                    MaterialKey: "mat-two",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: secondTexturePath,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1]),
            ],
            SourceObjectKey: objectIdentity);
    }

    private static ResoniteConstructionCityObject CreateHeightMapCityObject(
        string objectIdentity,
        IReadOnlyList<double> heightSamples)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "terrain",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Geometry: new ResoniteHeightMapGridGeometry(
                Width: 2,
                Height: 2,
                Size: new ResoniteFloat2(10.0, 10.0),
                MinHeight: 0.0,
                MaxHeight: 3.0,
                HeightSamples: heightSamples),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "heightmap-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: objectIdentity);
    }

    private static ResoniteImportedMesh CreateTriangleMesh(double firstY, double secondY, double thirdY)
    {
        return CreateTriangleMesh(firstY, secondY, thirdY, "triangle-material");
    }

    private static ResoniteImportedMesh CreateTriangleMesh(
        double firstY,
        double secondY,
        double thirdY,
        string materialKey)
    {
        return new ResoniteImportedMesh(
            Vertices:
            [
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, firstY, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(1.0, secondY, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, thirdY, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
            ],
            Submeshes:
            [
                new ResoniteMeshSubmesh(0, materialKey, [0, 1, 2]),
            ]);
    }

    private sealed class ReuseSessionSharedClient : IResoniteLinkClient
    {
        private readonly ReuseFakeSession session;

        public ReuseSessionSharedClient()
            : this(new ReuseFakeSession())
        {
        }

        public ReuseSessionSharedClient(ReuseFakeSession session)
        {
            this.session = session;
        }

        public int ConnectCallCount { get; private set; }
        public List<AddComponent> AddedComponents => session.AddedComponents;
        public List<AddSlot> AddedSlots => session.AddedSlots;
        public List<ImportMeshRawData> ImportedMeshes => session.ImportedMeshes;
        public List<string> ImportedTexturePaths => session.ImportedTexturePaths;
        public List<ResoniteRawTextureImport> ImportedRawTextures => session.ImportedRawTextures;
        public List<ResoniteRawHdrTextureImport> ImportedRawHdrTextures => session.ImportedRawHdrTextures;
        public Dictionary<string, Component> ComponentsById => session.ComponentsById;
        public Dictionary<string, Slot> SlotsById => session.SlotsById;
        public List<IReadOnlyList<DataModelOperation>> Batches => session.Batches;

        public void Dispose()
        {
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectCallCount++;
            return Task.CompletedTask;
        }

        public Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string createdComponentId = session.AllocateComponentId();
            lock (session.Gate)
            {
                request.Data.ID = createdComponentId;
                session.ComponentsById[createdComponentId] = request.Data;
                if (session.SlotsById.TryGetValue(request.ContainerSlotId, out Slot? containerSlot))
                {
                    containerSlot.Components ??= [];
                    containerSlot.Components.Add(request.Data);
                }
                session.AddedComponents.Add(request);
            }

            return Task.FromResult(createdComponentId);
        }

        public Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string createdSlotId = session.AllocateSlotId();
            lock (session.Gate)
            {
                request.Data.ID = createdSlotId;
                session.SlotsById[createdSlotId] = request.Data;
                session.AddedSlots.Add(request);
            }

            return Task.FromResult(createdSlotId);
        }

        public async Task RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                session.Batches.Add(operations.ToArray());
            }

            foreach (DataModelOperation operation in operations)
            {
                switch (operation)
                {
                    case AddSlot addSlot:
                        await AddSlotAsync(addSlot, cancellationToken);
                        break;
                    case AddComponent addComponent:
                        await AddComponentAsync(addComponent, cancellationToken);
                        break;
                    case UpdateComponent updateComponent:
                        await UpdateComponentAsync(updateComponent, cancellationToken);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported batch operation '{operation.GetType().Name}'.");
                }
            }
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Component? component;
            lock (session.Gate)
            {
                session.ComponentsById.TryGetValue(componentId, out component);
            }

            return Task.FromResult(component);
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(slotId, "Root", StringComparison.Ordinal))
            {
                return Task.FromResult<Slot?>(CreateSyntheticRoot(depth));
            }

            Slot? slot;
            lock (session.Gate)
            {
                session.SlotsById.TryGetValue(slotId, out slot);
            }

            return Task.FromResult(slot is null ? null : CloneSlot(slot, depth));
        }

        public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                session.ImportedMeshes.Add(request);
                return Task.FromResult(new Uri($"resdb:///mesh/{session.ImportedMeshes.Count - 1}", UriKind.Absolute));
            }
        }

        public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                switch (textureImport)
                {
                    case ResoniteFileTextureImport fileImport:
                        session.ImportedTexturePaths.Add(fileImport.AbsolutePath);
                        break;
                    case ResoniteRawTextureImport rawImport:
                        session.ImportedRawTextures.Add(rawImport);
                        if (rawImport.Identity is not null)
                        {
                            session.ImportedTexturePaths.Add(rawImport.Identity);
                        }

                        break;
                    case ResoniteRawHdrTextureImport rawHdrImport:
                        session.ImportedRawHdrTextures.Add(rawHdrImport);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported texture import type '{textureImport.GetType().Name}'.");
                }

                return Task.FromResult(new Uri($"resdb:///texture/{session.ImportedTexturePaths.Count + session.ImportedRawTextures.Count + session.ImportedRawHdrTextures.Count - 1}", UriKind.Absolute));
            }
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                if (!session.ComponentsById.TryGetValue(request.Data.ID, out Component? existingComponent))
                {
                    return Task.CompletedTask;
                }

                foreach ((string memberName, Member member) in request.Data.Members)
                {
                    existingComponent.Members[memberName] = member;
                }
            }

            return Task.CompletedTask;
        }

        private Slot CloneSlot(Slot source, int depth)
        {
            Slot clone = new()
            {
                ID = source.ID,
                Parent = source.Parent,
                Name = source.Name,
                Position = source.Position,
                Components = source.Components,
                Rotation = source.Rotation,
            };

            if (depth <= 0)
            {
                return clone;
            }

            lock (session.Gate)
            {
                clone.Children = session.SlotsById.Values
                    .Where(slot => string.Equals(slot.Parent?.TargetID, source.ID, StringComparison.Ordinal))
                    .Select(slot => CloneSlot(slot, depth - 1))
                    .ToList();
            }

            return clone;
        }

        private Slot CreateSyntheticRoot(int depth)
        {
            Slot root = new()
            {
                ID = "Root",
                Name = new Field_string
                {
                    Value = "Root",
                },
            };

            if (depth <= 0)
            {
                return root;
            }

            lock (session.Gate)
            {
                root.Children = session.SlotsById.Values
                    .Where(slot => string.Equals(slot.Parent?.TargetID, "Root", StringComparison.Ordinal))
                    .Select(slot => CloneSlot(slot, depth - 1))
                    .ToList();
            }

            return root;
        }
    }

    private sealed class ReuseFakeSession
    {
        private int nextComponentId;
        private int nextSlotId;

        public object Gate { get; } = new();

        public List<AddComponent> AddedComponents { get; } = [];

        public List<AddSlot> AddedSlots { get; } = [];

        public List<ImportMeshRawData> ImportedMeshes { get; } = [];

        public List<string> ImportedTexturePaths { get; } = [];

        public List<ResoniteRawTextureImport> ImportedRawTextures { get; } = [];

        public List<ResoniteRawHdrTextureImport> ImportedRawHdrTextures { get; } = [];

        public List<IReadOnlyList<DataModelOperation>> Batches { get; } = [];

        public Dictionary<string, Component> ComponentsById { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Slot> SlotsById { get; } = new(StringComparer.Ordinal);

        public string AllocateComponentId()
        {
            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"srv_component_{Interlocked.Increment(ref nextComponentId)}");
        }

        public string AllocateSlotId()
        {
            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"srv_slot_{Interlocked.Increment(ref nextSlotId)}");
        }
    }

    private sealed record CapturedScene(
        ResoniteConstructionMetadata Metadata,
        IReadOnlyList<ResoniteConstructionCityObject> CityObjects);

}

[CollectionDefinition(BundledCompanionTextureIsolationGroup.Name, DisableParallelization = true)]
public sealed class BundledCompanionTextureIsolationGroup
{
    public const string Name = "BundledCompanionTextureIsolation";
}
