using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;

using GeographicLib;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Plateau.ResoniteLink.Tests.Cli;

[Collection(BundledCompanionTextureIsolationGroup.Name)]
[Trait("Category", "Slow")]
[SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "The test helper owns builder disposal for all streaming execution paths.")]
public sealed class ResoniteLinkSceneBuilderTests
{
    private static readonly ConcurrentDictionary<PlateauImportRequest, CapturedResoniteScene> SceneCache = [];

    [Fact]
    public async Task BuildAsyncImportsAssetsAndBuildsLiveComponents()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));

        using FakeResoniteLinkClient fakeClient = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient);

        IReadOnlyList<string> destinations = await RunBuilderAsync(builder, scene);

        Assert.Single(destinations);
        Assert.InRange(fakeClient.ImportedTexturePaths.Count, 5, 7);
        Assert.Equal(scene.CityObjects.Count, fakeClient.ImportedMeshes.Count);
        Assert.Contains(fakeClient.AddedComponents, static request =>
            string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticMesh", StringComparison.Ordinal));
        Assert.Contains(fakeClient.AddedComponents, static request =>
            string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal));
        Assert.Contains(fakeClient.AddedComponents, static request =>
            string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal));
        Assert.Contains(fakeClient.AddedComponents, static request =>
            string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshCollider", StringComparison.Ordinal));
        Assert.Contains(fakeClient.AddedComponents, static request =>
            string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal));
        Assert.Contains(fakeClient.AddedComponents, static request =>
            string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.License", StringComparison.Ordinal));
        ResoniteConstructionCityObject buildingOne = Assert.Single(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Building One");
        string buildingLodSlotName = buildingOne.LodLevel.HasValue
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"LOD{buildingOne.LodLevel.Value}")
            : "LOD0";
        Assert.Contains("PLATEAU tokyo23ku/Assets", fakeClient.SlotPaths.Values);
        Assert.Contains("PLATEAU tokyo23ku/Assets/Common", fakeClient.SlotPaths.Values);
        Assert.Contains($"PLATEAU tokyo23ku/Assets/53394525/bldg/{buildingLodSlotName}/Building One", fakeClient.SlotPaths.Values);
        Assert.Contains($"PLATEAU tokyo23ku/53394525/bldg/{buildingLodSlotName}/Building One", fakeClient.SlotPaths.Values);
        Assert.DoesNotContain("PLATEAU tokyo23ku/Assets/Textures", fakeClient.SlotPaths.Values);
        Assert.DoesNotContain("PLATEAU tokyo23ku/Assets/Meshes", fakeClient.SlotPaths.Values);
        Assert.DoesNotContain(
            fakeClient.AddedSlots,
            request => string.Equals(request.Data.Name?.Value, "Building One Assets", StringComparison.Ordinal));

        IReadOnlyList<Component> staticMeshes = fakeClient.AddedComponents
            .Where(static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticMesh", StringComparison.Ordinal))
            .Select(static request => request.Data)
            .ToArray();
        Assert.All(staticMeshes, static component =>
        {
            Field_Uri url = Assert.IsType<Field_Uri>(component.Members["URL"]);
            Assert.StartsWith("resdb:///mesh/", url.Value.ToString(), StringComparison.Ordinal);
        });

        AddComponent[] staticTextureRequests = fakeClient.AddedComponents
            .Where(request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal))
            .ToArray();
        Assert.InRange(staticTextureRequests.Length, 4, 7);

        Assert.Contains(
            fakeClient.ImportedTexturePaths,
            path => string.Equals(
                path,
                Path.GetFullPath(Path.Combine(fixturePath, "udx/bldg/53394525/appearance/roof.png")),
                StringComparison.Ordinal));
        Assert.Contains(
            fakeClient.ImportedTexturePaths,
            path => BundledDefaultMaterialFamilies.FacadeVariants.Any(variant =>
                path.EndsWith(variant.Replace('/', Path.DirectorySeparatorChar), StringComparison.Ordinal)));
        Assert.Contains(
            fakeClient.ImportedTexturePaths,
            static path => path.EndsWith("_NormalGL.jpg", StringComparison.Ordinal));
        Assert.Contains(
            fakeClient.ImportedTexturePaths,
            static path => path.EndsWith("_Height.jpg", StringComparison.Ordinal));
        Assert.Contains(
            fakeClient.ImportedTexturePaths,
            static path => path.EndsWith("_Metallic.png", StringComparison.Ordinal));
        Assert.Empty(fakeClient.ImportedRawTextures);

        Assert.All(staticTextureRequests, request =>
        {
            Slot textureAssetSlot = fakeClient.SlotsById[request.ContainerSlotId];
            Assert.StartsWith(
                "PLATEAU tokyo23ku/Assets/",
                fakeClient.SlotPaths[textureAssetSlot.ID],
                StringComparison.Ordinal);
            Field_Uri textureUrl = Assert.IsType<Field_Uri>(request.Data.Members["URL"]);
            Assert.StartsWith("resdb:///texture/", textureUrl.Value.ToString(), StringComparison.Ordinal);
        });

        Component license = Assert.Single(
            fakeClient.AddedComponents.Where(request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.License", StringComparison.Ordinal))
                .Select(static request => request.Data));
        Field_bool requireCredit = Assert.IsType<Field_bool>(license.Members["RequireCredit"]);
        Assert.True(requireCredit.Value);
        Field_string creditString = Assert.IsType<Field_string>(license.Members["CreditString"]);
        Assert.Contains(scene.Metadata.Attribution.DatasetLicense.LicenseName, creditString.Value, StringComparison.Ordinal);
        Assert.Contains(scene.Metadata.Attribution.DatasetLicense.LicenseUrl, creditString.Value, StringComparison.Ordinal);
        Assert.Contains(scene.Metadata.Attribution.DatasetLicense.CreditText, creditString.Value, StringComparison.Ordinal);

        AddComponent[] meshAssetRequests = fakeClient.AddedComponents
            .Where(request =>
                string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticMesh", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(meshAssetRequests);
        Assert.Contains(
            meshAssetRequests,
            request => string.Equals(
                fakeClient.SlotPaths[request.ContainerSlotId],
                $"PLATEAU tokyo23ku/Assets/53394525/bldg/{buildingLodSlotName}/Building One",
                StringComparison.Ordinal));

        AddComponent[] materialRequests = fakeClient.AddedComponents
            .Where(request =>
                string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal)
                && request.ContainerSlotId != fakeClient.BuildingSlotIds["Building One"])
            .ToArray();
        Assert.InRange(materialRequests.Length, 2, 3);
        Assert.Contains(
            materialRequests,
            request =>
            {
                string slotPath = fakeClient.SlotPaths[request.ContainerSlotId];
                return slotPath.StartsWith("PLATEAU tokyo23ku/Assets/Common/", StringComparison.Ordinal)
                    && !string.Equals(slotPath, "PLATEAU tokyo23ku/Assets/Common", StringComparison.Ordinal)
                    && slotPath.Contains("_uv_", StringComparison.Ordinal);
            });
        Assert.Contains(
            materialRequests,
            request =>
            {
                string slotPath = fakeClient.SlotPaths[request.ContainerSlotId];
                return slotPath.StartsWith(
                    $"PLATEAU tokyo23ku/Assets/53394525/bldg/{buildingLodSlotName}/Building One/",
                    StringComparison.Ordinal);
            });

        Component meshRenderer = Assert.Single(
            fakeClient.AddedComponents.Where(request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal)
                    && string.Equals(request.ContainerSlotId, fakeClient.BuildingSlotIds["Building One"], StringComparison.Ordinal))
                .Select(static request => request.Data));
        SyncList materials = Assert.IsType<SyncList>(meshRenderer.Members["Materials"]);
        Assert.Equal(2, materials.Elements.Count);

        Component collider = Assert.Single(
            fakeClient.AddedComponents.Where(request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshCollider", StringComparison.Ordinal)
                    && string.Equals(request.ContainerSlotId, fakeClient.BuildingSlotIds["Building One"], StringComparison.Ordinal))
                .Select(static request => request.Data));
        Field_bool characterCollider = Assert.IsType<Field_bool>(collider.Members["CharacterCollider"]);
        Assert.True(characterCollider.Value);
    }

    [Fact]
    public async Task BuildAsyncCreatesIndependentSlotsAndComponentsWithoutDuplicateAdds()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));

        using FakeResoniteLinkClient fakeClient = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient);

        await RunBuilderAsync(builder, scene);

        Assert.Equal(
            2,
            fakeClient.AddedSlots.Count(request => string.Equals(request.Data.Name?.Value, "bldg", StringComparison.Ordinal)));
        Assert.Equal(
            2,
            fakeClient.AddedSlots.Count(request => string.Equals(request.Data.Name?.Value, "LOD2", StringComparison.Ordinal)));
        Assert.DoesNotContain(
            fakeClient.AddedSlots
                .GroupBy(static request => request.Data.ID, StringComparer.Ordinal),
            static group => group.Count() > 1);
        Assert.DoesNotContain(
            fakeClient.AddedComponents
                .GroupBy(static request => request.Data.ID, StringComparer.Ordinal),
            static group => group.Count() > 1);
    }

    [Fact]
    public async Task BuildAsyncUsesDedicatedMaterialsForDatasetTexturedMaterials()
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

        ResoniteConstructionMetadata metadata = new(
            SchemaVersion: "3.0",
            WorldName: "PLATEAU reuse-test",
            Request: new PlateauImportRequest(
                Dataset: "reuse-test",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetDirectory.Path,
                ServerUri: null),
            SourceDataset: new PlateauSourceDataset(
                PackageNames: ["bldg"],
                SourceFiles: [textureOne, textureTwo],
                TerrainTextureOverlays: []),
            Attribution: new ResoniteAttribution(
                DatasetLicense: new ResoniteLicenseComponentMetadata(
                    RequireCredit: true,
                    CreditText: "credit",
                    LicenseName: "license",
                    LicenseUrl: "https://example.invalid/license"),
                MaterialLicenses: []),
            LocalOrigin: new ResoniteLocalOrigin(35.6875, 139.69375, 0.0));
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "albedo-only-building",
            DisplayName: "Albedo Only Building",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
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
                    TexturePath: textureOne,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
                new ResoniteMaterialBinding(
                    MaterialKey: "mat-two",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: textureTwo,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1]),
            ],
            SourceObjectKey: "albedo-only-building");
        CapturedResoniteScene scene = new(metadata, [cityObject]);

        using FakeResoniteLinkClient fakeClient = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient);

        await RunBuilderAsync(builder, scene);

        AddComponent[] materialRequests = fakeClient.AddedComponents
            .Where(request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(materialRequests);
        Assert.DoesNotContain(
            fakeClient.AddedComponents,
            request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal));

        Component meshRenderer = Assert.Single(
            fakeClient.AddedComponents.Where(request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal)
                    && string.Equals(request.ContainerSlotId, fakeClient.BuildingSlotIds["Albedo Only Building"], StringComparison.Ordinal))
                .Select(static request => request.Data));
        Assert.DoesNotContain("MaterialPropertyBlocks", meshRenderer.Members.Keys);
        SyncList materials = Assert.IsType<SyncList>(meshRenderer.Members["Materials"]);
        Assert.Equal(2, materials.Elements.Count);
    }

    [Fact]
    public async Task BuildAsyncDoesNotUseMaterialPropertyBlocksForMixedMaterialStrategies()
    {
        using TemporaryDirectory datasetDirectory = new();
        string datasetTexture = "textures/albedo-one.png";
        Directory.CreateDirectory(Path.Combine(datasetDirectory.Path, "textures"));
        await WriteSolidColorTextureAsync(
            Path.Combine(datasetDirectory.Path, datasetTexture),
            new Rgba32(255, 0, 0, 255));

        ResoniteConstructionMetadata metadata = new(
            SchemaVersion: "3.0",
            WorldName: "PLATEAU mixed-property-block-test",
            Request: new PlateauImportRequest(
                Dataset: "mixed-property-block-test",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetDirectory.Path,
                ServerUri: null),
            SourceDataset: new PlateauSourceDataset(
                PackageNames: ["bldg"],
                SourceFiles: [datasetTexture],
                TerrainTextureOverlays: []),
            Attribution: new ResoniteAttribution(
                DatasetLicense: new ResoniteLicenseComponentMetadata(
                    RequireCredit: true,
                    CreditText: "credit",
                    LicenseName: "license",
                    LicenseUrl: "https://example.invalid/license"),
                MaterialLicenses: []),
            LocalOrigin: new ResoniteLocalOrigin(35.6875, 139.69375, 0.0));
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "mixed-material-building",
            DisplayName: "Mixed Material Building",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
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
                    TexturePath: datasetTexture,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
                new ResoniteMaterialBinding(
                    MaterialKey: "mat-two",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: BundledDefaultMaterialFamilies.FacadeVariants[0],
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1]),
            ],
            SourceObjectKey: "mixed-material-building");
        CapturedResoniteScene scene = new(metadata, [cityObject]);

        using FakeResoniteLinkClient fakeClient = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient);

        await RunBuilderAsync(builder, scene);

        Component meshRenderer = Assert.Single(
            fakeClient.AddedComponents.Where(request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal)
                    && string.Equals(request.ContainerSlotId, fakeClient.BuildingSlotIds["Mixed Material Building"], StringComparison.Ordinal))
                .Select(static request => request.Data));
        Assert.DoesNotContain("MaterialPropertyBlocks", meshRenderer.Members.Keys);
        Assert.DoesNotContain(
            fakeClient.AddedComponents,
            request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildAsyncOmitsMaterialPropertyBlocksForMixedRenderers()
    {
        using TemporaryDirectory datasetDirectory = new();
        string textureOne = "textures/albedo-one.png";
        Directory.CreateDirectory(Path.Combine(datasetDirectory.Path, "textures"));
        await WriteSolidColorTextureAsync(
            Path.Combine(datasetDirectory.Path, textureOne),
            new Rgba32(255, 0, 0, 255));

        ResoniteConstructionMetadata metadata = new(
            SchemaVersion: "3.0",
            WorldName: "PLATEAU reuse-test",
            Request: new PlateauImportRequest(
                Dataset: "reuse-test",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetDirectory.Path,
                ServerUri: null),
            SourceDataset: new PlateauSourceDataset(
                PackageNames: ["bldg"],
                SourceFiles: [textureOne],
                TerrainTextureOverlays: []),
            Attribution: new ResoniteAttribution(
                DatasetLicense: new ResoniteLicenseComponentMetadata(
                    RequireCredit: true,
                    CreditText: "credit",
                    LicenseName: "license",
                    LicenseUrl: "https://example.invalid/license"),
                MaterialLicenses: []),
            LocalOrigin: new ResoniteLocalOrigin(35.6875, 139.69375, 0.0));
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "mixed-building",
            DisplayName: "Mixed Building",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
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
                    TexturePath: textureOne,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
                new ResoniteMaterialBinding(
                    MaterialKey: "mat-two",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: BundledDefaultMaterialFamilies.FacadeVariants[0],
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1]),
            ],
            SourceObjectKey: "mixed-building");
        CapturedResoniteScene scene = new(metadata, [cityObject]);

        using FakeResoniteLinkClient fakeClient = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient);

        await RunBuilderAsync(builder, scene);

        Component meshRenderer = Assert.Single(
            fakeClient.AddedComponents.Where(request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal)
                    && string.Equals(request.ContainerSlotId, fakeClient.BuildingSlotIds["Mixed Building"], StringComparison.Ordinal))
                .Select(static request => request.Data));
        Assert.DoesNotContain("MaterialPropertyBlocks", meshRenderer.Members.Keys);
    }

    [Fact]
    public async Task BuildAsyncUsesTriplanarMaterialForBundledRoadFallback()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDatasetMixedObjects");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));

        using FakeResoniteLinkClient fakeClient = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient);

        await RunBuilderAsync(builder, scene);

        Component[] triplanarMaterials = fakeClient.AddedComponents
            .Where(static request =>
                string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_TriplanarMetallic", StringComparison.Ordinal))
            .Select(static request => request.Data)
            .ToArray();
        Assert.NotEmpty(triplanarMaterials);
        Assert.All(
            triplanarMaterials,
            static triplanarMaterial =>
            {
                Assert.IsType<Field_float2>(triplanarMaterial.Members["TextureScale"]);
                Assert.IsType<Field_float2>(triplanarMaterial.Members["TextureOffset"]);
                Assert.IsType<Field_float>(triplanarMaterial.Members["TriplanarBlendPower"]);
                Assert.IsType<Field_bool>(triplanarMaterial.Members["ObjectSpace"]);
                Assert.IsType<Reference>(triplanarMaterial.Members["NormalMap"]);
                Assert.IsType<Field_float>(triplanarMaterial.Members["NormalScale"]);
                Assert.IsType<Reference>(triplanarMaterial.Members["MetallicMap"]);
                Assert.IsType<Reference>(triplanarMaterial.Members["OcclusionMap"]);
                Assert.DoesNotContain("HeightMap", triplanarMaterial.Members.Keys);
                Assert.DoesNotContain("HeightScale", triplanarMaterial.Members.Keys);
            });
        Assert.Contains(
            triplanarMaterials,
            static triplanarMaterial =>
            {
                Field_float2 scale = Assert.IsType<Field_float2>(triplanarMaterial.Members["TextureScale"]);
                bool matchesAsphalt020L =
                    Math.Abs(scale.Value.x - (float)BundledDefaultMaterialProfiles.Asphalt020LTilesPerMeter.X) < 0.000001f
                    && Math.Abs(scale.Value.y - (float)BundledDefaultMaterialProfiles.Asphalt020LTilesPerMeter.Y) < 0.000001f;
                bool matchesAsphalt023L =
                    Math.Abs(scale.Value.x - (float)BundledDefaultMaterialProfiles.Asphalt023LTilesPerMeter.X) < 0.000001f
                    && Math.Abs(scale.Value.y - (float)BundledDefaultMaterialProfiles.Asphalt023LTilesPerMeter.Y) < 0.000001f;
                return matchesAsphalt020L || matchesAsphalt023L;
            });

        Component uvFacadeMaterial = Assert.Single(
            fakeClient.AddedComponents.Where(static request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal)
                    && request.Data.Members.ContainsKey("AlbedoTexture")
                    && request.Data.Members.ContainsKey("TextureScale"))
                .Select(static request => request.Data));
        Field_float2 uvScale = Assert.IsType<Field_float2>(uvFacadeMaterial.Members["TextureScale"]);
        Assert.Equal((float)(1.0 / 13.0), uvScale.Value.x, 6);
        Assert.Equal((float)(1.0 / 13.0), uvScale.Value.y, 6);
        Assert.IsType<Reference>(uvFacadeMaterial.Members["NormalMap"]);
        Assert.IsType<Reference>(uvFacadeMaterial.Members["HeightMap"]);
        Assert.IsType<Field_float>(uvFacadeMaterial.Members["HeightScale"]);
        Field_float uvHeightScale = Assert.IsType<Field_float>(uvFacadeMaterial.Members["HeightScale"]);
        Assert.Equal(0.002f, uvHeightScale.Value);
    }

    [Fact]
    public async Task BuildAsyncUsesGridMeshForDemHeightMap()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDatasetMixedObjects");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null,
                PackageNames: ["dem"],
                DemTerrainMode: DemTerrainMode.HeightMap,
                DemHeightmapMetersPerVertex: 10.0,
                DemHeightmapMaxResolution: 16));

        using FakeResoniteLinkClient fakeClient = new();
        StubTerrainTextureAssetGenerator terrainTextureAssetGenerator = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient,
            terrainTextureAssetGenerator);

        await RunBuilderAsync(builder, scene);

        Assert.Empty(fakeClient.ImportedMeshes);
        Assert.DoesNotContain(
            fakeClient.AddedComponents,
            static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticMesh", StringComparison.Ordinal));

        AddComponent gridMeshRequest = Assert.Single(
            fakeClient.AddedComponents,
            static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.GridMesh", StringComparison.Ordinal));
        Field_int2 points = Assert.IsType<Field_int2>(gridMeshRequest.Data.Members["Points"]);
        Assert.True(points.Value.x >= 2);
        Assert.True(points.Value.y >= 2);
        Assert.IsType<Field_float2>(gridMeshRequest.Data.Members["Size"]);
        Assert.IsType<Field_float>(gridMeshRequest.Data.Members["DisplacementMagnitude"]);
        Reference displacementTexture = Assert.IsType<Reference>(gridMeshRequest.Data.Members["DisplacementTexture"]);

        AddComponent heightTextureRequest = Assert.Single(
            fakeClient.AddedComponents,
            request =>
                string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal)
                && request.Data.Members.ContainsKey("Readable")
                && request.Data.Members.ContainsKey("Uncompressed"));
        Assert.False(string.IsNullOrWhiteSpace(displacementTexture.TargetID));
        Assert.True(Assert.IsType<Field_bool>(heightTextureRequest.Data.Members["Readable"]).Value);
        Assert.True(Assert.IsType<Field_bool>(heightTextureRequest.Data.Members["Uncompressed"]).Value);
        Assert.True(Assert.IsType<Field_bool>(heightTextureRequest.Data.Members["DirectLoad"]).Value);
        Assert.False(Assert.IsType<Field_bool>(heightTextureRequest.Data.Members["MipMaps"]).Value);
        Assert.Equal("Clamp", Assert.IsType<Field_Enum>(heightTextureRequest.Data.Members["WrapModeU"]).Value);
        Assert.Equal("Clamp", Assert.IsType<Field_Enum>(heightTextureRequest.Data.Members["WrapModeV"]).Value);
        Assert.Equal("Point", Assert.IsType<Field_Nullable_Enum>(heightTextureRequest.Data.Members["FilterMode"]).Value);
        Assert.DoesNotContain("SourceFingerprint", heightTextureRequest.Data.Members.Keys);

        string reliefSlotId = fakeClient.BuildingSlotIds["Relief One"];
        Slot reliefSlot = fakeClient.SlotsById[reliefSlotId];
        Field_floatQ rotation = Assert.IsType<Field_floatQ>(reliefSlot.Rotation);
        Assert.Equal((float)Math.Sqrt(0.5), rotation.Value.x, 6);
        Assert.Equal((float)Math.Sqrt(0.5), rotation.Value.w, 6);

        Component meshRenderer = Assert.Single(
            fakeClient.AddedComponents.Where(request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal)
                    && string.Equals(request.ContainerSlotId, reliefSlotId, StringComparison.Ordinal))
                .Select(static request => request.Data));
        string renderedGridMeshId = Assert.IsType<Reference>(meshRenderer.Members["Mesh"]).TargetID;
        Assert.False(string.IsNullOrWhiteSpace(renderedGridMeshId));

        Component collider = Assert.Single(
            fakeClient.AddedComponents.Where(request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshCollider", StringComparison.Ordinal)
                    && string.Equals(request.ContainerSlotId, reliefSlotId, StringComparison.Ordinal))
                .Select(static request => request.Data));
        string colliderGridMeshId = Assert.IsType<Reference>(collider.Members["Mesh"]).TargetID;
        Assert.Equal(renderedGridMeshId, colliderGridMeshId);
    }

    [Fact]
    public async Task BuildAsyncWritesHeightMapTextureAsHdrRawWithBlueOnlyScaledDisplacement()
    {
        const string dataset = "tokyo23ku";
        const string meshCode = "53394525";
        const string slotKey = "dem_heightmap_test";
        const string sourceObjectKey = "dem_heightmap_test_source";

        ResoniteConstructionMetadata metadata = new(
            SchemaVersion: "3.0",
            WorldName: $"PLATEAU {dataset} {meshCode}",
            Request: new PlateauImportRequest(
                Dataset: dataset,
                MeshCode: meshCode,
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: TestData.GetFixturePath("LocalPlateauDataset"),
                ServerUri: null),
            SourceDataset: new PlateauSourceDataset(
                PackageNames: ["dem"],
                SourceFiles: ["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"],
                TerrainTextureOverlays: []),
            Attribution: new ResoniteAttribution(
                DatasetLicense: new ResoniteLicenseComponentMetadata(
                    RequireCredit: true,
                    CreditText: "PLATEAU Open Data Terms",
                    LicenseName: "PLATEAU Open Data Terms",
                    LicenseUrl: "https://www.mlit.go.jp/plateau/site-policy/"),
                MaterialLicenses: []),
            LocalOrigin: new ResoniteLocalOrigin(35.6875, 139.69375, 0.0));
        CapturedResoniteScene scene = new(
            metadata,
            [
                new ResoniteConstructionCityObject(
                    SlotKey: slotKey,
                    DisplayName: "HeightMap Test",
                    PackageName: "dem",
                    ActualMeshCode: meshCode,
                    LodLevel: 0,
                    Transform: new ResoniteTransform(
                        new ResoniteFloat3(0.0, 0.0, 0.0),
                        new ResoniteFloatQ(Math.Sqrt(0.5), 0.0, 0.0, Math.Sqrt(0.5))),
                    Geometry: new ResoniteHeightMapGridGeometry(
                        Width: 2,
                        Height: 2,
                        Size: new ResoniteFloat2(10.0, 10.0),
                        MinHeight: 0.0,
                        MaxHeight: 10.0,
                        HeightSamples: [0.0, 10.0, 0.0, 10.0]),
                    Materials:
                    [
                        new ResoniteMaterialBinding(
                            MaterialKey: "dem-heightmap-material",
                            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                            MaterialType: ResoniteMaterialType.Standard,
                            TexturePath: null,
                            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                            Projection: ResoniteMaterialProjection.Uv,
                            DepthOffset: null,
                            SubmeshIndices: [0]),
                    ],
                    SourceObjectKey: sourceObjectKey),
            ]);

        using FakeResoniteLinkClient fakeClient = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient);

        using TemporaryDirectory workDirectory = new();
        await RunBuilderAsync(builder, scene, workDirectory.Path);

        ResoniteRawHdrTextureImport heightMapTexture = Assert.Single(fakeClient.ImportedRawHdrTextures);
        float[] pixels = new float[heightMapTexture.RawRgbaFloatBytes.Length / sizeof(float)];
        Buffer.BlockCopy(heightMapTexture.RawRgbaFloatBytes, 0, pixels, 0, heightMapTexture.RawRgbaFloatBytes.Length);

        Assert.Equal(2, heightMapTexture.Width);
        Assert.Equal(2, heightMapTexture.Height);
        Assert.Equal(0.0f, pixels[0]);
        Assert.Equal(0.0f, pixels[1]);
        Assert.Equal(3.0f, pixels[2]);
        Assert.Equal(1.0f, pixels[3]);
        Assert.Equal(0.0f, pixels[4]);
        Assert.Equal(0.0f, pixels[5]);
        Assert.Equal(0.0f, pixels[6]);
        Assert.Equal(1.0f, pixels[7]);
        Assert.Equal(0.0f, pixels[8]);
        Assert.Equal(0.0f, pixels[9]);
        Assert.Equal(3.0f, pixels[10]);
        Assert.Equal(1.0f, pixels[11]);
        Assert.Equal(0.0f, pixels[12]);
        Assert.Equal(0.0f, pixels[13]);
        Assert.Equal(0.0f, pixels[14]);
        Assert.Equal(1.0f, pixels[15]);
    }

    [Fact]
    public async Task BuildAsyncPlacesHeightMapDisplacementTextureOnDedicatedAssetSlot()
    {
        const string dataset = "tokyo23ku";
        const string meshCode = "53394525";
        const string overlayTexturePath = "terrain://dem/gsi-seamlessphoto/00000-00000-TEST";

        ResoniteConstructionMetadata metadata = new(
            SchemaVersion: "3.0",
            WorldName: $"PLATEAU {dataset} {meshCode}",
            Request: new PlateauImportRequest(
                Dataset: dataset,
                MeshCode: meshCode,
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: TestData.GetFixturePath("LocalPlateauDataset"),
                ServerUri: null),
            SourceDataset: new PlateauSourceDataset(
                PackageNames: ["dem"],
                SourceFiles: ["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"],
                TerrainTextureOverlays:
                [
                    new TerrainTextureOverlay(
                        TexturePath: overlayTexturePath,
                        PackageName: "dem",
                        UrlTemplate: LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureUrlTemplate,
                        ZoomLevel: LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureZoomLevel,
                        GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
                        MaxTextureSize: LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureMaxSize),
                ]),
            Attribution: new ResoniteAttribution(
                DatasetLicense: new ResoniteLicenseComponentMetadata(
                    RequireCredit: true,
                    CreditText: "PLATEAU Open Data Terms",
                    LicenseName: "PLATEAU Open Data Terms",
                    LicenseUrl: "https://www.mlit.go.jp/plateau/site-policy/"),
                MaterialLicenses: []),
            LocalOrigin: new ResoniteLocalOrigin(35.6875, 139.69375, 0.0));
        CapturedResoniteScene scene = new(
            metadata,
            [
                new ResoniteConstructionCityObject(
                    SlotKey: "dem_heightmap_overlay_test",
                    DisplayName: "HeightMap Overlay Test",
                    PackageName: "dem",
                    ActualMeshCode: meshCode,
                    LodLevel: 0,
                    Transform: new ResoniteTransform(
                        new ResoniteFloat3(0.0, 0.0, 0.0),
                        new ResoniteFloatQ(Math.Sqrt(0.5), 0.0, 0.0, Math.Sqrt(0.5))),
                    Geometry: new ResoniteHeightMapGridGeometry(
                        Width: 2,
                        Height: 2,
                        Size: new ResoniteFloat2(10.0, 10.0),
                        MinHeight: 0.0,
                        MaxHeight: 10.0,
                        HeightSamples: [0.0, 10.0, 0.0, 10.0]),
                    Materials:
                    [
                        new ResoniteMaterialBinding(
                            MaterialKey: "dem-heightmap-overlay-material",
                            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                            MaterialType: ResoniteMaterialType.Standard,
                            TexturePath: overlayTexturePath,
                            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                            Projection: ResoniteMaterialProjection.Uv,
                            DepthOffset: null,
                            SubmeshIndices: [0]),
                    ],
                    SourceObjectKey: "dem_heightmap_overlay_test_source"),
            ]);

        using FakeResoniteLinkClient fakeClient = new();
        StubTerrainTextureAssetGenerator terrainTextureAssetGenerator = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient,
            terrainTextureAssetGenerator);

        using TemporaryDirectory workDirectory = new();
        await RunBuilderAsync(builder, scene, workDirectory.Path);

        AddComponent[] textureRequests = fakeClient.AddedComponents
            .Where(static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, textureRequests.Length);

        AddComponent displacementTextureRequest = Assert.Single(
            textureRequests,
            request => string.Equals(
                fakeClient.SlotsById[request.ContainerSlotId].Name?.Value,
                "HeightMap Overlay Test_heightmap",
                StringComparison.Ordinal));
        AddComponent materialTextureRequest = Assert.Single(
            textureRequests,
            request => !string.Equals(
                fakeClient.SlotsById[request.ContainerSlotId].Name?.Value,
                "HeightMap Overlay Test_heightmap",
                StringComparison.Ordinal));

        Slot displacementTextureSlot = fakeClient.SlotsById[displacementTextureRequest.ContainerSlotId];
        Slot materialTextureSlot = fakeClient.SlotsById[materialTextureRequest.ContainerSlotId];

        Assert.Equal("HeightMap Overlay Test_heightmap", displacementTextureSlot.Name?.Value);
        Assert.NotEqual("HeightMap Overlay Test_heightmap", materialTextureSlot.Name?.Value);
        Assert.NotEqual(displacementTextureRequest.ContainerSlotId, materialTextureRequest.ContainerSlotId);
        Assert.DoesNotContain("/Assets/Common/", fakeClient.SlotPaths[displacementTextureSlot.ID], StringComparison.Ordinal);
        Assert.DoesNotContain("/Assets/Common/", fakeClient.SlotPaths[materialTextureSlot.ID], StringComparison.Ordinal);
        Assert.StartsWith(
            "PLATEAU tokyo23ku/Assets/53394525/dem/LOD0/HeightMap Overlay Test/",
            fakeClient.SlotPaths[materialTextureSlot.ID],
            StringComparison.Ordinal);
    }


    [Fact]
    public async Task BuildAsyncAppliesMaterialDepthOffsetForTerrainAlignedOverlays()
    {
        CapturedResoniteScene scene = new(
            new ResoniteConstructionMetadata(
                SchemaVersion: "3.0",
                WorldName: "PLATEAU tokyo23ku 53394525",
                Request: new PlateauImportRequest(
                    Dataset: "tokyo23ku",
                    MeshCode: "53394525",
                    SourceKind: DatasetSourceKind.Local,
                    LocalSourcePath: TestData.GetFixturePath("LocalPlateauDataset"),
                    ServerUri: null),
                SourceDataset: new PlateauSourceDataset(
                    PackageNames: ["tran"],
                    SourceFiles: ["udx/tran/53394525/plateau_tokyo23ku_tran_53394525.gml"],
                    TerrainTextureOverlays: []),
                Attribution: new ResoniteAttribution(
                    DatasetLicense: new ResoniteLicenseComponentMetadata(
                        RequireCredit: true,
                        CreditText: "PLATEAU Open Data Terms",
                        LicenseName: "PLATEAU Open Data Terms",
                        LicenseUrl: "https://www.mlit.go.jp/plateau/site-policy/"),
                    MaterialLicenses: []),
                LocalOrigin: new ResoniteLocalOrigin(35.0, 139.0, 0.0)),
            [
                new ResoniteConstructionCityObject(
                    SlotKey: "terrain_road",
                    DisplayName: "Terrain Road",
                    PackageName: "tran",
                    ActualMeshCode: "53394525",
                    LodLevel: 1,
                    Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
                    Mesh: new ResoniteImportedMesh(
                        Vertices:
                        [
                            new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                            new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                            new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                        ],
                        Submeshes:
                        [
                            new ResoniteMeshSubmesh(0, "terrain-road-material", [0, 1, 2]),
                        ]),
                    Materials:
                    [
                        new ResoniteMaterialBinding(
                            MaterialKey: "terrain-road-material",
                            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                            MaterialType: ResoniteMaterialType.Standard,
                            TexturePath: null,
                            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                            Projection: ResoniteMaterialProjection.Triplanar,
                            DepthOffset: LocalCityGmlResonitePlanBuilder.DefaultTerrainAlignedMaterialDepthOffset,
                            SubmeshIndices: [0]),
                    ]),
            ]);

        using FakeResoniteLinkClient fakeClient = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient);

        await RunBuilderAsync(builder, scene);

        Component material = Assert.Single(
            fakeClient.AddedComponents.Where(static request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_TriplanarMetallic", StringComparison.Ordinal))
                .Select(static request => request.Data));
        Field_float offsetFactor = Assert.IsType<Field_float>(material.Members["OffsetFactor"]);
        Field_float offsetUnits = Assert.IsType<Field_float>(material.Members["OffsetUnits"]);
        Assert.Equal((float)LocalCityGmlResonitePlanBuilder.DefaultTerrainAlignedMaterialDepthOffset.Factor, offsetFactor.Value);
        Assert.Equal((float)LocalCityGmlResonitePlanBuilder.DefaultTerrainAlignedMaterialDepthOffset.Units, offsetUnits.Value);
    }

    [Fact]
    public async Task BuildAsyncDoesNotPlaceDatasetScopedMaterialWithoutTextureInCommonAssets()
    {
        CapturedResoniteScene scene = new(
            new ResoniteConstructionMetadata(
                SchemaVersion: "3.0",
                WorldName: "PLATEAU tokyo23ku 53394525",
                Request: new PlateauImportRequest(
                    Dataset: "tokyo23ku",
                    MeshCode: "53394525",
                    SourceKind: DatasetSourceKind.Local,
                    LocalSourcePath: TestData.GetFixturePath("LocalPlateauDataset"),
                    ServerUri: null),
                SourceDataset: new PlateauSourceDataset(
                    PackageNames: ["tran"],
                    SourceFiles: ["udx/tran/53394525/plateau_tokyo23ku_tran_53394525.gml"],
                    TerrainTextureOverlays: []),
                Attribution: new ResoniteAttribution(
                    DatasetLicense: new ResoniteLicenseComponentMetadata(
                        RequireCredit: true,
                        CreditText: "PLATEAU Open Data Terms",
                        LicenseName: "PLATEAU Open Data Terms",
                        LicenseUrl: "https://www.mlit.go.jp/plateau/site-policy/"),
                    MaterialLicenses: []),
                LocalOrigin: new ResoniteLocalOrigin(35.0, 139.0, 0.0)),
            [
                new ResoniteConstructionCityObject(
                    SlotKey: "dataset-flat-material",
                    DisplayName: "Dataset Flat Material",
                    PackageName: "tran",
                    ActualMeshCode: "53394525",
                    LodLevel: 1,
                    Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
                    Mesh: new ResoniteImportedMesh(
                        Vertices:
                        [
                            new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                            new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                            new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                        ],
                        Submeshes:
                        [
                            new ResoniteMeshSubmesh(0, "dataset-flat-material", [0, 1, 2]),
                        ]),
                    Materials:
                    [
                        new ResoniteMaterialBinding(
                            MaterialKey: "dataset-flat-material",
                            BaseColor: new ResoniteColor(0.4, 0.5, 0.6, 1.0),
                            MaterialType: ResoniteMaterialType.Standard,
                            TexturePath: null,
                            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                            Projection: ResoniteMaterialProjection.Uv,
                            DepthOffset: null,
                            SubmeshIndices: [0]),
                    ]),
            ]);

        using FakeResoniteLinkClient fakeClient = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient);

        await RunBuilderAsync(builder, scene);

        AddComponent materialRequest = Assert.Single(
            fakeClient.AddedComponents,
            static request => string.Equals(
                request.Data.ComponentType,
                "[FrooxEngine]FrooxEngine.PBS_Metallic",
                StringComparison.Ordinal));
        string materialPath = fakeClient.SlotPaths[materialRequest.ContainerSlotId];
        Assert.StartsWith("PLATEAU tokyo23ku/Assets/53394525/tran/LOD1/", materialPath, StringComparison.Ordinal);
        Assert.DoesNotContain("/Assets/Common", materialPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsyncUsesWireframeMaterialForOverlayCityObjects()
    {
        CapturedResoniteScene scene = new(
            new ResoniteConstructionMetadata(
                SchemaVersion: "3.0",
                WorldName: "PLATEAU tokyo23ku 53394525",
                Request: new PlateauImportRequest(
                    Dataset: "tokyo23ku",
                    MeshCode: "53394525",
                    SourceKind: DatasetSourceKind.Local,
                    LocalSourcePath: TestData.GetFixturePath("LocalPlateauDataset"),
                    ServerUri: null),
                SourceDataset: new PlateauSourceDataset(
                    PackageNames: ["area"],
                    SourceFiles: ["udx/area/53394525/plateau_tokyo23ku_area_53394525.gml"],
                    TerrainTextureOverlays: []),
                Attribution: new ResoniteAttribution(
                    DatasetLicense: new ResoniteLicenseComponentMetadata(
                        RequireCredit: true,
                        CreditText: "PLATEAU Open Data Terms",
                        LicenseName: "PLATEAU Open Data Terms",
                        LicenseUrl: "https://www.mlit.go.jp/plateau/site-policy/"),
                    MaterialLicenses: []),
                LocalOrigin: new ResoniteLocalOrigin(35.0, 139.0, 0.0)),
            [
                new ResoniteConstructionCityObject(
                    SlotKey: "area_overlay",
                    DisplayName: "Area Overlay",
                    PackageName: "area",
                    ActualMeshCode: "53394525",
                    LodLevel: 1,
                    Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
                    Mesh: new ResoniteImportedMesh(
                        Vertices:
                        [
                            new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                            new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                            new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                        ],
                        Submeshes:
                        [
                            new ResoniteMeshSubmesh(0, "area-wireframe-material", [0, 1, 2]),
                        ]),
                    Materials:
                    [
                        new ResoniteMaterialBinding(
                            MaterialKey: "area-wireframe-material",
                            BaseColor: new ResoniteColor(0.2, 0.4, 0.6, 0.5),
                            MaterialType: ResoniteMaterialType.Wireframe,
                            TexturePath: null,
                            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                            Projection: ResoniteMaterialProjection.Uv,
                            DepthOffset: null,
                            SubmeshIndices: [0]),
                    ]),
            ]);

        using FakeResoniteLinkClient fakeClient = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient);

        await RunBuilderAsync(builder, scene);

        Component material = Assert.Single(
            fakeClient.AddedComponents.Where(static request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.WireframeMaterial", StringComparison.Ordinal))
                .Select(static request => request.Data));
        Field_float thickness = Assert.IsType<Field_float>(material.Members["Thickness"]);
        Field_bool screenSpace = Assert.IsType<Field_bool>(material.Members["ScreenSpace"]);
        Field_colorX lineColor = Assert.IsType<Field_colorX>(material.Members["LineColor"]);
        Field_colorX fillColor = Assert.IsType<Field_colorX>(material.Members["FillColor"]);
        Field_bool doubleSided = Assert.IsType<Field_bool>(material.Members["DoubleSided"]);

        Assert.Equal(0.01f, thickness.Value);
        Assert.True(screenSpace.Value);
        Assert.DoesNotContain("AlbedoColor", material.Members.Keys);
        Assert.DoesNotContain("Smoothness", material.Members.Keys);
        Assert.Equal(0.2f, lineColor.Value.r, 6);
        Assert.Equal(0.4f, lineColor.Value.g, 6);
        Assert.Equal(0.6f, lineColor.Value.b, 6);
        Assert.Equal(0.5f, lineColor.Value.a, 6);
        Assert.Equal(0.04f, fillColor.Value.a, 6);
        Assert.True(doubleSided.Value);
    }

    [Fact]
    public async Task BuildAsyncUsesVertexColorMaterialAndImportsMeshColorsForVegetation()
    {
        CapturedResoniteScene scene = new(
            new ResoniteConstructionMetadata(
                SchemaVersion: "3.0",
                WorldName: "PLATEAU tokyo23ku 53394525",
                Request: new PlateauImportRequest(
                    Dataset: "tokyo23ku",
                    MeshCode: "53394525",
                    SourceKind: DatasetSourceKind.Local,
                    LocalSourcePath: TestData.GetFixturePath("LocalPlateauDataset"),
                    ServerUri: null),
                SourceDataset: new PlateauSourceDataset(
                    PackageNames: ["veg"],
                    SourceFiles: ["udx/veg/53394525/plateau_tokyo23ku_veg_53394525.gml"],
                    TerrainTextureOverlays: []),
                Attribution: new ResoniteAttribution(
                    DatasetLicense: new ResoniteLicenseComponentMetadata(
                        RequireCredit: true,
                        CreditText: "PLATEAU Open Data Terms",
                        LicenseName: "PLATEAU Open Data Terms",
                        LicenseUrl: "https://www.mlit.go.jp/plateau/site-policy/"),
                    MaterialLicenses: []),
                LocalOrigin: new ResoniteLocalOrigin(35.0, 139.0, 0.0)),
            [
                new ResoniteConstructionCityObject(
                    SlotKey: "veg_tree",
                    DisplayName: "Vegetation",
                    PackageName: "veg",
                    ActualMeshCode: "53394525",
                    LodLevel: 2,
                    Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
                    Mesh: new ResoniteImportedMesh(
                        Vertices:
                        [
                            new ResoniteMeshVertex(
                                new ResoniteFloat3(0.0, 0.0, 0.0),
                                new ResoniteFloat3(0.0, 1.0, 0.0),
                                new ResoniteFloat2(0.0, 0.0),
                                new ResoniteColor(0.2, 0.6, 0.2, 1.0)),
                            new ResoniteMeshVertex(
                                new ResoniteFloat3(1.0, 0.0, 0.0),
                                new ResoniteFloat3(0.0, 1.0, 0.0),
                                new ResoniteFloat2(1.0, 0.0),
                                new ResoniteColor(0.2, 0.6, 0.2, 1.0)),
                            new ResoniteMeshVertex(
                                new ResoniteFloat3(0.0, 0.0, 1.0),
                                new ResoniteFloat3(0.0, 1.0, 0.0),
                                new ResoniteFloat2(0.0, 1.0),
                                new ResoniteColor(0.2, 0.6, 0.2, 1.0)),
                        ],
                        Submeshes:
                        [
                            new ResoniteMeshSubmesh(0, "veg-vertex-color", [0, 1, 2]),
                        ]),
                    Materials:
                    [
                        new ResoniteMaterialBinding(
                            MaterialKey: "veg-vertex-color",
                            BaseColor: new ResoniteColor(0.2, 0.6, 0.2, 1.0),
                            MaterialType: ResoniteMaterialType.VertexColor,
                            TexturePath: null,
                            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                            Projection: ResoniteMaterialProjection.Uv,
                            DepthOffset: null,
                            SubmeshIndices: [0]),
                    ]),
            ]);

        using FakeResoniteLinkClient fakeClient = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient);

        await RunBuilderAsync(builder, scene);

        Assert.Contains(
            fakeClient.AddedComponents,
            static request => string.Equals(
                request.Data.ComponentType,
                "[FrooxEngine]FrooxEngine.PBS_VertexColorMetallic",
                StringComparison.Ordinal));

        ImportMeshRawData importedMesh = Assert.Single(fakeClient.ImportedMeshes);
        Assert.True(importedMesh.HasColors);
        Assert.Equal(3, importedMesh.Colors.Length);
        Assert.Equal(0.2f, importedMesh.Colors[0].r, 6);
        Assert.Equal(0.6f, importedMesh.Colors[0].g, 6);
        Assert.Equal(0.2f, importedMesh.Colors[0].b, 6);
        Assert.Equal(1.0f, importedMesh.Colors[0].a, 6);
    }

    [Fact]
    public async Task BuildAsyncUsesGreenPbsMaterialForColorlessVegetation()
    {
        CapturedResoniteScene scene = new(
            new ResoniteConstructionMetadata(
                SchemaVersion: "3.0",
                WorldName: "PLATEAU tokyo23ku 53394525",
                Request: new PlateauImportRequest(
                    Dataset: "tokyo23ku",
                    MeshCode: "53394525",
                    SourceKind: DatasetSourceKind.Local,
                    LocalSourcePath: TestData.GetFixturePath("LocalPlateauDataset"),
                    ServerUri: null),
                SourceDataset: new PlateauSourceDataset(
                    PackageNames: ["veg"],
                    SourceFiles: ["udx/veg/53394525/plateau_tokyo23ku_veg_53394525.gml"],
                    TerrainTextureOverlays: []),
                Attribution: new ResoniteAttribution(
                    DatasetLicense: new ResoniteLicenseComponentMetadata(
                        RequireCredit: true,
                        CreditText: "PLATEAU Open Data Terms",
                        LicenseName: "PLATEAU Open Data Terms",
                        LicenseUrl: "https://www.mlit.go.jp/plateau/site-policy/"),
                    MaterialLicenses: []),
                LocalOrigin: new ResoniteLocalOrigin(35.0, 139.0, 0.0)),
            [
                new ResoniteConstructionCityObject(
                    SlotKey: "veg_default",
                    DisplayName: "Vegetation Default",
                    PackageName: "veg",
                    ActualMeshCode: "53394525",
                    LodLevel: 2,
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
                        Submeshes:
                        [
                            new ResoniteMeshSubmesh(0, "veg-green-default", [0, 1, 2]),
                        ]),
                    Materials:
                    [
                        new ResoniteMaterialBinding(
                            MaterialKey: "veg-green-default",
                            BaseColor: new ResoniteColor(0.32, 0.58, 0.24, 1.0),
                            MaterialType: ResoniteMaterialType.Standard,
                            TexturePath: null,
                            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                            Projection: ResoniteMaterialProjection.Uv,
                            DepthOffset: null,
                            SubmeshIndices: [0]),
                    ]),
            ]);

        using FakeResoniteLinkClient fakeClient = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient);

        await RunBuilderAsync(builder, scene);

        Component material = Assert.Single(
            fakeClient.AddedComponents.Where(static request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal)
                    && !request.Data.Members.ContainsKey("AlbedoTexture"))
                .Select(static request => request.Data));
        Field_colorX albedoColor = Assert.IsType<Field_colorX>(material.Members["AlbedoColor"]);
        Assert.Equal(0.32f, albedoColor.Value.r, 6);
        Assert.Equal(0.58f, albedoColor.Value.g, 6);
        Assert.Equal(0.24f, albedoColor.Value.b, 6);
    }

    [Fact]
    public async Task BuildAsyncDisablesColliderForNoCollisionCityObjects()
    {
        CapturedResoniteScene scene = new(
            new ResoniteConstructionMetadata(
                SchemaVersion: "3.0",
                WorldName: "PLATEAU tokyo23ku 53394525",
                Request: new PlateauImportRequest(
                    Dataset: "tokyo23ku",
                    MeshCode: "53394525",
                    SourceKind: DatasetSourceKind.Local,
                    LocalSourcePath: TestData.GetFixturePath("LocalPlateauDataset"),
                    ServerUri: null),
                SourceDataset: new PlateauSourceDataset(
                    PackageNames: ["tran"],
                    SourceFiles: ["udx/tran/53394525/plateau_tokyo23ku_tran_53394525.gml"],
                    TerrainTextureOverlays: []),
                Attribution: new ResoniteAttribution(
                    DatasetLicense: new ResoniteLicenseComponentMetadata(
                        RequireCredit: true,
                        CreditText: "PLATEAU Open Data Terms",
                        LicenseName: "PLATEAU Open Data Terms",
                        LicenseUrl: "https://www.mlit.go.jp/plateau/site-policy/"),
                    MaterialLicenses: []),
                LocalOrigin: new ResoniteLocalOrigin(35.0, 139.0, 0.0)),
            [
                new ResoniteConstructionCityObject(
                    SlotKey: "tran_marking",
                    DisplayName: "Road Marking",
                    PackageName: "tran",
                    ActualMeshCode: "53394525",
                    LodLevel: 2,
                    Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
                    Mesh: new ResoniteImportedMesh(
                        Vertices:
                        [
                            new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0), new ResoniteColor(1.0, 1.0, 1.0, 1.0)),
                            new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0), new ResoniteColor(1.0, 1.0, 1.0, 1.0)),
                            new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0), new ResoniteColor(1.0, 1.0, 1.0, 1.0)),
                        ],
                        Submeshes:
                        [
                            new ResoniteMeshSubmesh(0, "tran-marking", [0, 1, 2]),
                        ]),
                    Materials:
                    [
                        new ResoniteMaterialBinding(
                            MaterialKey: "tran-marking",
                            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                            MaterialType: ResoniteMaterialType.VertexColor,
                            TexturePath: null,
                            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                            Projection: ResoniteMaterialProjection.Uv,
                            DepthOffset: LocalCityGmlResonitePlanBuilder.DefaultTerrainAlignedMaterialDepthOffset,
                            SubmeshIndices: [0]),
                    ],
                    CollisionEnabled: false),
            ]);

        using FakeResoniteLinkClient fakeClient = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient);

        await RunBuilderAsync(builder, scene);

        Component collider = Assert.Single(
            fakeClient.AddedComponents.Where(request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshCollider", StringComparison.Ordinal))
                .Select(static request => request.Data));
        Field_Enum type = Assert.IsType<Field_Enum>(collider.Members["Type"]);
        Field_bool characterCollider = Assert.IsType<Field_bool>(collider.Members["CharacterCollider"]);
        Assert.Equal("NoCollision", type.Value);
        Assert.False(characterCollider.Value);
    }

    [Fact]
    public async Task BuildAsyncCreatesConcreteAnchorRootWhenRequestMeshCodeIsRegex()
    {
        using FakeResoniteLinkClient fakeClient = new();
        CapturedResoniteScene scene = CreateRegexRequestScene();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient);

        await RunBuilderAsync(builder, scene);

        Slot datasetSlot = FindSlotByName(fakeClient.SlotsById, "PLATEAU tokyo23ku");
        Assert.Equal(["Assets", "53394525", "533945"], await GetDirectChildNamesAsync(fakeClient, datasetSlot.ID));
        Slot anchorSlot = FindDirectChildSlotByName(fakeClient.SlotsById, datasetSlot.ID, "53394525");
        Field_float3 anchorPosition = Assert.IsType<Field_float3>(anchorSlot.Position);
        Assert.Equal(0.0f, anchorPosition.Value.x, precision: 4);
        Assert.Equal(0.0f, anchorPosition.Value.y, precision: 4);
        Assert.Equal(0.0f, anchorPosition.Value.z, precision: 4);
        Assert.DoesNotContain(
            fakeClient.SlotsById.Values,
            static slot =>
                !string.IsNullOrWhiteSpace(slot.Name?.Value)
                && slot.Name.Value.StartsWith("regex-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildAsyncPlacesRegexParentMeshRootFromAnchorOffset()
    {
        using FakeResoniteLinkClient fakeClient = new();
        CapturedResoniteScene scene = CreateRegexRequestScene(
            "5339452[56]",
            new ResoniteLocalOrigin(35.6875, 139.70625, 0.0));
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient);

        await RunBuilderAsync(builder, scene);

        Slot datasetSlot = FindSlotByName(fakeClient.SlotsById, "PLATEAU tokyo23ku");
        Slot anchorMeshRootSlot = FindDirectChildSlotByName(fakeClient.SlotsById, datasetSlot.ID, "53394525");
        Slot parentMeshRootSlot = FindDirectChildSlotByName(fakeClient.SlotsById, datasetSlot.ID, "533945");
        Field_float3 anchorMeshRootPosition = Assert.IsType<Field_float3>(anchorMeshRootSlot.Position);
        Field_float3 parentMeshRootPosition = Assert.IsType<Field_float3>(parentMeshRootSlot.Position);
        Assert.True(PlateauMeshCode.TryGetCenter("53394525", out ResoniteLocalOrigin anchorMeshCenter));
        Assert.True(PlateauMeshCode.TryGetCenter("533945", out ResoniteLocalOrigin parentMeshCenter));
        ResoniteFloat3 expectedPosition = ComputeOriginOffsetForTest(anchorMeshCenter, parentMeshCenter) with { Y = 0.0 };

        Assert.Equal(0.0f, anchorMeshRootPosition.Value.x, precision: 4);
        Assert.Equal(0.0f, anchorMeshRootPosition.Value.y, precision: 4);
        Assert.Equal(0.0f, anchorMeshRootPosition.Value.z, precision: 4);
        Assert.Equal((float)expectedPosition.X, parentMeshRootPosition.Value.x, precision: 4);
        Assert.Equal(0.0f, parentMeshRootPosition.Value.y, precision: 4);
        Assert.Equal((float)expectedPosition.Z, parentMeshRootPosition.Value.z, precision: 4);
    }

    [Fact]
    public async Task BuildAsyncUsesConcreteMeshCodeCompletionForRegexSelectors()
    {
        using FakeResoniteLinkClient fakeClient = new();
        IReadOnlyList<string> destinations = await RunBuilderAsync(
            new ResoniteLinkSceneBuilder(new Uri("ws://localhost:12345/"), 1, ResoniteLinkSendDiagnostics.Disabled, () => fakeClient),
            CreateRegexRequestScene("5339452(56)"));

        Slot datasetSlot = FindSlotByName(fakeClient.SlotsById, "PLATEAU tokyo23ku");
        Slot completionAnchorSlot = FindDirectChildSlotByName(fakeClient.SlotsById, datasetSlot.ID, "53394525");
        Assert.Contains("PLATEAU tokyo23ku/53394525", fakeClient.SlotPaths.Values);
        Assert.Equal(
            [$"ws://localhost:12345/#{completionAnchorSlot.ID}"],
            destinations);
        Assert.DoesNotContain(
            fakeClient.SlotsById.Values,
            static slot =>
                !string.IsNullOrWhiteSpace(slot.Name?.Value)
                && slot.Name.Value.StartsWith("regex-", StringComparison.Ordinal));
        Assert.DoesNotContain("PLATEAU tokyo23ku/5339452[56]", fakeClient.SlotPaths.Values);
        Assert.DoesNotContain("PLATEAU tokyo23ku/5339452(56)", fakeClient.SlotPaths.Values);
    }

    [Fact]
    public async Task BuildAsyncImportsGeneratedDemTerrainTexture()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDatasetMixedObjects");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));

        using FakeResoniteLinkClient fakeClient = new();
        StubTerrainTextureAssetGenerator terrainTextureAssetGenerator = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient,
            terrainTextureAssetGenerator);

        using TemporaryDirectory workDirectory = new();
        await RunBuilderAsync(builder, scene, workDirectory.Path);

        TerrainTextureOverlay requestedOverlay = Assert.Single(terrainTextureAssetGenerator.RequestedOverlays);
        Assert.StartsWith(
            LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath,
            requestedOverlay.TexturePath,
            StringComparison.Ordinal);

        ResoniteRawTextureImport builtInTexture = Assert.Single(
            fakeClient.ImportedRawTextures,
            static texture => texture.Identity is not null
                && texture.Identity.StartsWith(LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath, StringComparison.Ordinal));
        Assert.True(builtInTexture.Width > 0);
        Assert.True(builtInTexture.Height > 0);
        Assert.Equal("sRGB", builtInTexture.ColorProfile);
        Assert.Equal(builtInTexture.Width * builtInTexture.Height * 4, builtInTexture.RawRgba32Bytes.Length);
    }

    [Fact]
    public async Task BuildAsyncCanDistributeImportsAcrossMultipleConnections()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));
        ResoniteLinkSceneBuilder.DispatchLaneAllocator dispatchLaneAllocator = new(2);
        int[] dispatchLanes = scene.CityObjects
            .Select(dispatchLaneAllocator.GetLane)
            .Distinct()
            .OrderBy(static lane => lane)
            .ToArray();
        Assert.Equal([0, 1], dispatchLanes);

        FakeResoniteLinkSession session = new();
        FakeResoniteLinkClient[] clients =
        [
            new(session),
            new(session),
        ];
        int factoryIndex = 0;

        try
        {
            ResoniteLinkSceneBuilder builder = new(
                new Uri("ws://localhost:12345/"),
                2,
                ResoniteLinkSendDiagnostics.Disabled,
                () => clients[Interlocked.Increment(ref factoryIndex) - 1]);

            await RunBuilderAsync(builder, scene);
        }
        finally
        {
            foreach (FakeResoniteLinkClient client in clients)
            {
                client.Dispose();
            }
        }

        Assert.True(clients.All(client => client.ConnectCallCount == 1));
        Assert.True(clients.All(client => client.ImportedMeshCount > 0));
        Assert.Equal(scene.CityObjects.Count, clients.Sum(client => client.ImportedMeshCount));
    }

    [Fact]
    public async Task BuildAsyncReportsEachSentCityObjectAtInfoLevel()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));
        using FakeResoniteLinkClient fakeClient = new();
        List<string> progressMessages = [];
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient,
            progressReporter: progressMessages.Add);

        await RunBuilderAsync(builder, scene);

        string[] sentCityObjectMessages = progressMessages
            .Where(static message => message.StartsWith("[live][info] Sent city object ", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(scene.CityObjects.Count, sentCityObjectMessages.Length);
    }

    [Fact]
    public async Task BeginAsyncDoesNotWaitForDeferredWorkerConnections()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));

        FakeResoniteLinkSession session = new();
        using FakeResoniteLinkClient firstClient = new(session);
        using DelayedConnectClient secondClient = new();
        int factoryIndex = 0;
        using TemporaryDirectory workDirectory = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            2,
            ResoniteLinkSendDiagnostics.Disabled,
            () => Interlocked.Increment(ref factoryIndex) switch
            {
                1 => firstClient,
                2 => secondClient,
                _ => throw new InvalidOperationException("Unexpected client factory call."),
            });

        try
        {
            await builder.BeginAsync(scene.Metadata, workDirectory.Path).WaitAsync(TimeSpan.FromSeconds(2));
            await secondClient.ConnectStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, firstClient.ConnectCallCount);
            Assert.Equal(1, secondClient.ConnectCallCount);
        }
        finally
        {
            secondClient.AllowConnect();
            await builder.DisposeAsync();
        }
    }

    [Fact]
    public async Task CompleteAsyncFailsWhenWorkerConnectionTimesOut()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));

        FakeResoniteLinkSession session = new();
        using FakeResoniteLinkClient firstClient = new(session);
        using UncooperativeConnectClient secondClient = new();
        int factoryIndex = 0;
        using TemporaryDirectory workDirectory = new();
        List<string> progressMessages = [];
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            2,
            ResoniteLinkSendDiagnostics.Disabled,
            () => Interlocked.Increment(ref factoryIndex) switch
            {
                1 => (IResoniteLinkClient)firstClient,
                2 => secondClient,
                _ => throw new InvalidOperationException("Unexpected client factory call."),
            },
            progressReporter: progressMessages.Add);

        try
        {
            await builder.BeginAsync(scene.Metadata, workDirectory.Path).WaitAsync(TimeSpan.FromSeconds(2));
            await secondClient.ConnectStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
                await builder.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(8)));

            Assert.Contains("did not connect within", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            await builder.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        }

        Assert.Equal(1, secondClient.DisposeCallCount);
        Assert.DoesNotContain(
            progressMessages,
            static message => message.Contains("DisposeAsync timed out waiting for send lanes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompleteAsyncCancelsTimedOutWorkerConnect()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));

        FakeResoniteLinkSession session = new();
        using FakeResoniteLinkClient firstClient = new(session);
        using CancellationAwareConnectClient secondClient = new();
        int factoryIndex = 0;
        using TemporaryDirectory workDirectory = new();
        await using ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            2,
            ResoniteLinkSendDiagnostics.Disabled,
            () => Interlocked.Increment(ref factoryIndex) switch
            {
                1 => (IResoniteLinkClient)firstClient,
                2 => secondClient,
                _ => throw new InvalidOperationException("Unexpected client factory call."),
            });

        await builder.BeginAsync(scene.Metadata, workDirectory.Path).WaitAsync(TimeSpan.FromSeconds(2));
        await secondClient.ConnectStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await builder.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(8)));

        await secondClient.ConnectCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task DisposeAsyncDisposesClientsEvenWhenLaneDrainTimesOut()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));

        using NonCancelableBlockingResoniteLinkClient client = new();
        using TemporaryDirectory workDirectory = new();
        await using ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => client);

        await builder.BeginAsync(scene.Metadata, workDirectory.Path).WaitAsync(TimeSpan.FromSeconds(2));
        await builder.ProcessCityObjectAsync(scene.CityObjects[0]).WaitAsync(TimeSpan.FromSeconds(2));
        await client.ImportStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Stopwatch stopwatch = Stopwatch.StartNew();
        await builder.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        stopwatch.Stop();

        Assert.Equal(1, client.DisposeCallCount);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(900));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await builder.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void GetDispatchLaneKeepsSameCityObjectIdentityOnSameConnection()
    {
        ResoniteLinkSceneBuilder.DispatchLaneAllocator dispatchLaneAllocator = new(4);
        ResoniteConstructionCityObject first = CreateDispatchTestCityObject(
            slotKey: "bldg_1",
            sourceObjectKey: "shared-source");
        ResoniteConstructionCityObject second = CreateDispatchTestCityObject(
            slotKey: "bldg_2",
            sourceObjectKey: "shared-source");

        int firstLane = dispatchLaneAllocator.GetLane(first);
        int secondLane = dispatchLaneAllocator.GetLane(second);

        Assert.Equal(firstLane, secondLane);
    }

    [Fact]
    public async Task ProcessCityObjectAsyncQueuesWorkBeforeLiveMeshImportCompletes()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));

        using BlockingResoniteLinkClient blockingClient = new();
        await using ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => blockingClient);

        using TemporaryDirectory workDirectory = new();
        await builder.BeginAsync(scene.Metadata, workDirectory.Path);
        await builder.ProcessCityObjectAsync(scene.CityObjects[0]);

        Task<IReadOnlyList<string>> completionTask = builder.CompleteAsync();
        Assert.False(completionTask.IsCompleted);

        blockingClient.ReleaseMeshImports();

        IReadOnlyList<string> destinations = await completionTask;
        Assert.Single(destinations);
    }

    [Fact]
    public async Task BeginAsyncReusesExistingDatasetRootAssetsAndCommonBySlotNameAcrossRuns()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));
        FakeResoniteLinkSession session = new();

        ResoniteLinkSceneBuilder firstBuilder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => new FakeResoniteLinkClient(session));
        await RunBuilderAsync(firstBuilder, scene);

        ResoniteLinkSceneBuilder secondBuilder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => new FakeResoniteLinkClient(session));
        await RunBuilderAsync(secondBuilder, scene);

        Assert.Equal(1, session.SlotPaths.Values.Count(path => string.Equals(path, "PLATEAU tokyo23ku", StringComparison.Ordinal)));
        Assert.Equal(1, session.SlotPaths.Values.Count(path => string.Equals(path, "PLATEAU tokyo23ku/Assets", StringComparison.Ordinal)));
        Assert.Equal(1, session.SlotPaths.Values.Count(path => string.Equals(path, "PLATEAU tokyo23ku/Assets/Common", StringComparison.Ordinal)));
        Assert.Equal(1, session.SlotPaths.Values.Count(path => string.Equals(path, "PLATEAU tokyo23ku/53394525", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task BuildAsyncSurfacesLaneFailureWhenCityObjectSendFails()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDatasetMixedObjects");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));
        List<string> progressMessages = [];
        FakeResoniteLinkSession session = new();
        int remainingMeshImportFailures = 4;

        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => new FailingImportMeshSharedClient(
                session,
                () => Interlocked.Decrement(ref remainingMeshImportFailures) >= 0),
            progressReporter: progressMessages.Add);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await RunBuilderAsync(builder, scene));

        Assert.Contains("Simulated mesh import failure.", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            progressMessages,
            static message => message.Contains("[live][error] Send lane 1/1 failed:", StringComparison.Ordinal));
        Assert.Contains(
            progressMessages,
            static message => message.Contains("[live][debug] Creating dataset root, asset groups, and anchor slots.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildAsyncDoesNotLeavePresentationSlotWhenGeometryImportFails()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));
        FakeResoniteLinkSession session = new();

        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => new FailingImportMeshSharedClient(session, static () => true));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await RunBuilderAsync(builder, scene));

        Assert.DoesNotContain(
            session.SlotPaths.Values,
            static path => string.Equals(path, "PLATEAU tokyo23ku/53394525/bldg/LOD2/Building One", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildAsyncUsesResponseCanonicalIdsWhenCreateResponsesDiffer()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));
        FakeResoniteLinkSession session = new();

        using ResponseAuthoritativeIdClient client = new(session);
        IReadOnlyList<string> destinations = await RunBuilderAsync(
            new ResoniteLinkSceneBuilder(
                new Uri("ws://localhost:12345/"),
                1,
                ResoniteLinkSendDiagnostics.Disabled,
                () => client),
            scene);

        Assert.Single(destinations);
        Assert.Equal(0, client.RejectedAliasMutationCount);
        Assert.Equal(scene.CityObjects.Count, client.BatchMutationCount);
        Assert.NotEmpty(client.ReturnedSlotResponseIds);
        Assert.NotEmpty(client.ReturnedComponentResponseIds);
        Assert.All(client.ReturnedSlotResponseIds, responseId => Assert.Contains(responseId, session.SlotsById.Keys));
        Assert.All(client.ReturnedComponentResponseIds, responseId => Assert.Contains(responseId, session.ComponentsById.Keys));

        List<Component> meshRenderers = session.ComponentsById.Values
            .Where(component =>
                string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(meshRenderers);
        Assert.All(
            meshRenderers,
            meshRenderer =>
            {
                string meshComponentId = Assert.IsType<Reference>(meshRenderer.Members["Mesh"]).TargetID;
                Assert.Contains(meshComponentId, client.ReturnedComponentResponseIds);
            });

        List<Component> meshColliders = session.ComponentsById.Values
            .Where(component =>
                string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MeshCollider", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(meshColliders);
        Assert.All(
            meshColliders,
            meshCollider =>
            {
                string colliderMeshComponentId = Assert.IsType<Reference>(meshCollider.Members["Mesh"]).TargetID;
                Assert.Contains(colliderMeshComponentId, client.ReturnedComponentResponseIds);
            });
    }

    [Fact]
    public async Task BuildAsyncFailsWhenUnresolvedBatchComponentResponseFails()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));
        FakeResoniteLinkSession session = new();

        using ResponseAuthoritativeIdClient client = new(
            session,
            failedBatchComponentType: "[FrooxEngine]FrooxEngine.MeshCollider");
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await RunBuilderAsync(
                new ResoniteLinkSceneBuilder(
                    new Uri("ws://localhost:12345/"),
                    1,
                    ResoniteLinkSendDiagnostics.Disabled,
                    () => client),
                scene));

        Assert.Contains("validate component '[FrooxEngine]FrooxEngine.MeshCollider'", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, client.FailedBatchResponseCount);
    }

    [Fact]
    public async Task BuildAsyncIgnoresSameNameSiblingsDuringCreateCanonicalization()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));
        FakeResoniteLinkSession session = new();

        using ResponseAuthoritativeIdClient client = new(session, injectPreexistingPresentationSibling: true);
        IReadOnlyList<string> destinations = await RunBuilderAsync(
            new ResoniteLinkSceneBuilder(
                new Uri("ws://localhost:12345/"),
                1,
                ResoniteLinkSendDiagnostics.Disabled,
                () => client),
            scene);

        Assert.Single(destinations);
        Assert.True(client.PreexistingPresentationSiblingInjected);
        Assert.Equal(
            2,
            session.SlotsById.Values.Count(slot =>
                string.Equals(slot.Name?.Value, "Building One", StringComparison.Ordinal)
                && string.Equals(session.SlotPaths[slot.Parent!.TargetID], "PLATEAU tokyo23ku/53394525/bldg/LOD2", StringComparison.Ordinal)));
        Assert.Contains(
            session.AddedSlots,
            addSlot =>
                string.Equals(addSlot.Data.Name?.Value, "Building One", StringComparison.Ordinal)
                && addSlot.Data.Parent is not null
                && string.Equals(session.SlotPaths[addSlot.Data.Parent.TargetID], "PLATEAU tokyo23ku/53394525/bldg/LOD2", StringComparison.Ordinal));
        Assert.Equal(0, client.RejectedAliasMutationCount);
        Assert.Equal(scene.CityObjects.Count, client.BatchMutationCount);
    }

    [Fact]
    public async Task BuildAsyncUsesOneDataModelBatchPerCityObject()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));

        using FakeResoniteLinkClient client = new();
        IReadOnlyList<string> destinations = await RunBuilderAsync(
            new ResoniteLinkSceneBuilder(
                new Uri("ws://localhost:12345/"),
                1,
                ResoniteLinkSendDiagnostics.Disabled,
                () => client),
            scene);

        Assert.Single(destinations);
        Assert.Equal(scene.CityObjects.Count, client.Batches.Count);
        Assert.All(client.Batches, batch =>
        {
            Assert.Single(
                batch.OfType<AddComponent>(),
                static operation => string.Equals(
                    operation.Data.ComponentType,
                    "[FrooxEngine]FrooxEngine.MeshRenderer",
                    StringComparison.Ordinal));
            Assert.Single(
                batch.OfType<AddComponent>(),
                static operation => string.Equals(
                    operation.Data.ComponentType,
                    "[FrooxEngine]FrooxEngine.MeshCollider",
                    StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task BuildAsyncUsesGloballyUniqueBatchLocalIdsAcrossParallelWorkers()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));
        FakeResoniteLinkSession session = new();
        HashSet<string> claimedLocalIds = new(StringComparer.Ordinal);

        IReadOnlyList<string> destinations = await RunBuilderAsync(
            new ResoniteLinkSceneBuilder(
                new Uri("ws://localhost:12345/"),
                2,
                ResoniteLinkSendDiagnostics.Disabled,
                () => new GlobalLocalIdRejectingBatchClient(session, claimedLocalIds)),
            scene);

        Assert.Single(destinations);
        Assert.Equal(scene.CityObjects.Count, session.Batches.Count);
    }

    [Fact]
    public async Task BuildAsyncAppendsDifferentMeshCodeUsingExistingMeshRootAsAnchor()
    {
        FakeResoniteLinkSession session = new();

        await RunBuilderAsync(
            new ResoniteLinkSceneBuilder(
                new Uri("ws://localhost:12345/"),
                1,
                ResoniteLinkSendDiagnostics.Disabled,
                () => new FakeResoniteLinkClient(session)),
            CreateAppendScene("53394525", "Building 25"));
        await RunBuilderAsync(
            new ResoniteLinkSceneBuilder(
                new Uri("ws://localhost:12345/"),
                1,
                ResoniteLinkSendDiagnostics.Disabled,
                () => new FakeResoniteLinkClient(session)),
            CreateAppendScene("53394526", "Building 26"));

        Slot datasetSlot = FindSlotByName(session.SlotsById, "PLATEAU tokyo23ku");
        Slot firstMeshRootSlot = FindDirectChildSlotByName(session.SlotsById, datasetSlot.ID, "53394525");
        Slot secondMeshRootSlot = FindDirectChildSlotByName(session.SlotsById, datasetSlot.ID, "53394526");
        Field_float3 firstMeshRootPosition = Assert.IsType<Field_float3>(firstMeshRootSlot.Position);
        Field_float3 secondMeshRootPosition = Assert.IsType<Field_float3>(secondMeshRootSlot.Position);
        Assert.True(PlateauMeshCode.TryGetCenter("53394525", out ResoniteLocalOrigin firstCenter));
        Assert.True(PlateauMeshCode.TryGetCenter("53394526", out ResoniteLocalOrigin secondCenter));
        ResoniteFloat3 expectedOffset = ComputeOriginOffsetForTest(firstCenter, secondCenter) with { Y = 0.0 };

        Assert.Equal(0.0f, firstMeshRootPosition.Value.x, precision: 4);
        Assert.Equal(0.0f, firstMeshRootPosition.Value.y, precision: 4);
        Assert.Equal(0.0f, firstMeshRootPosition.Value.z, precision: 4);
        Assert.Equal((float)expectedOffset.X, secondMeshRootPosition.Value.x, precision: 4);
        Assert.Equal(0.0f, secondMeshRootPosition.Value.y, precision: 4);
        Assert.Equal((float)expectedOffset.Z, secondMeshRootPosition.Value.z, precision: 4);
    }

    [Fact]
    public async Task BeginAsyncReusesExistingCompletionMeshRootWhenVisibilityLags()
    {
        FakeResoniteLinkSession session = new();

        await RunBuilderAsync(
            new ResoniteLinkSceneBuilder(
                new Uri("ws://localhost:12345/"),
                1,
                ResoniteLinkSendDiagnostics.Disabled,
                () => new FakeResoniteLinkClient(session)),
            CreateAppendScene("53394525", "Building 25"));
        await RunBuilderAsync(
            new ResoniteLinkSceneBuilder(
                new Uri("ws://localhost:12345/"),
                1,
                ResoniteLinkSendDiagnostics.Disabled,
                () => new FakeResoniteLinkClient(session)),
            CreateAppendScene("53394526", "Building 26"));

        await using ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => new LaggyExistingCompletionMeshRootClient(session, "53394525", hiddenPollCount: 2));
        using TemporaryDirectory workDirectory = new();

        await builder.BeginAsync(CreateAppendScene("53394525", "Building 25 replay").Metadata, workDirectory.Path);

        Slot datasetSlot = FindSlotByName(session.SlotsById, "PLATEAU tokyo23ku");
        Assert.Single(
            session.SlotsById.Values,
            slot => string.Equals(slot.Parent?.TargetID, datasetSlot.ID, StringComparison.Ordinal)
                && string.Equals(slot.Name?.Value, "53394525", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BeginAsyncReusesExistingDatasetRootByNameUnderRoot()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));
        FakeResoniteLinkSession session = new();
        string existingDatasetRootId = session.AllocateSlotId();
        session.SlotsById[existingDatasetRootId] = new Slot
        {
            ID = existingDatasetRootId,
            Parent = new Reference
            {
                TargetID = "Root",
            },
            Name = new Field_string
            {
                Value = "PLATEAU tokyo23ku",
            },
        };
        session.SlotPaths[existingDatasetRootId] = "PLATEAU tokyo23ku";

        using FakeResoniteLinkClient fakeClient = new(session);
        await using ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient);

        using TemporaryDirectory workDirectory = new();
        await builder.BeginAsync(scene.Metadata, workDirectory.Path);

        Assert.DoesNotContain(
            fakeClient.AddedSlots,
            static request => string.Equals(request.Data.Name?.Value, "PLATEAU tokyo23ku", StringComparison.Ordinal));
        Assert.Contains(
            fakeClient.AddedSlots,
            request => string.Equals(request.Data.Parent?.TargetID, existingDatasetRootId, StringComparison.Ordinal)
                && string.Equals(request.Data.Name?.Value, "Assets", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BeginAsyncReusesExistingDatasetRootWhenRootVisibilityLags()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));
        FakeResoniteLinkSession session = new();
        string existingDatasetRootId = session.AllocateSlotId();
        session.SlotsById[existingDatasetRootId] = new Slot
        {
            ID = existingDatasetRootId,
            Parent = new Reference
            {
                TargetID = "Root",
            },
            Name = new Field_string
            {
                Value = "PLATEAU tokyo23ku",
            },
        };
        session.SlotPaths[existingDatasetRootId] = "PLATEAU tokyo23ku";

        await using ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => new LaggyExistingDatasetRootClient(session, "PLATEAU tokyo23ku", hiddenPollCount: 25));

        using TemporaryDirectory workDirectory = new();
        await builder.BeginAsync(scene.Metadata, workDirectory.Path);

        Assert.Single(
            session.SlotsById.Values,
            slot => string.Equals(slot.Parent?.TargetID, "Root", StringComparison.Ordinal)
                && string.Equals(slot.Name?.Value, "PLATEAU tokyo23ku", StringComparison.Ordinal));
        Assert.DoesNotContain(
            session.AddedSlots,
            static request => string.Equals(request.Data.Name?.Value, "PLATEAU tokyo23ku", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildAsyncWaitsForSharedParentVisibilityAcrossWorkerSessions()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDatasetMixedObjects");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));
        VisibilityLagSession session = new();
        await using ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            2,
            ResoniteLinkSendDiagnostics.Disabled,
            () => new VisibilityLagClient(session));
        using TemporaryDirectory workDirectory = new();

        await builder.BeginAsync(scene.Metadata, workDirectory.Path);
        foreach (ResoniteConstructionCityObject cityObject in scene.CityObjects.Take(2))
        {
            await builder.ProcessCityObjectAsync(cityObject);
        }

        IReadOnlyList<string> destinations = await builder.CompleteAsync();

        Assert.Single(destinations);
        Assert.Equal(0, session.RejectedInvisibleParentMutationCount);
    }

    [Fact]
    public async Task BuildAsyncProcessesSharedParentsAfterTheyPropagateToWorkers()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDatasetMixedObjects");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));
        VisibilityLagSession session = new();
        VisibilityLagClient[] clients =
        [
            new(session),
            new(session),
        ];
        int factoryIndex = 0;

        try
        {
            ResoniteLinkSceneBuilder builder = new(
                new Uri("ws://localhost:12345/"),
                2,
                ResoniteLinkSendDiagnostics.Disabled,
                () => clients[Interlocked.Increment(ref factoryIndex) - 1]);

            await RunBuilderAsync(builder, scene);
        }
        finally
        {
            foreach (VisibilityLagClient client in clients)
            {
                client.Dispose();
            }
        }

        Assert.True(session.ImportedMeshCount > 0);
        Assert.Equal(0, session.RejectedInvisibleParentMutationCount);
        Assert.Contains(
            session.SlotsById.Values,
            slot => string.Equals(slot.Name?.Value, "LOD1", StringComparison.Ordinal));
        Assert.Contains(
            session.ComponentsById.Values,
            component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal));
    }

    private static List<DataModelOperation> ResolveBatchLocalSlotReferences(
        IReadOnlyList<DataModelOperation> operations,
        Func<string> allocateSlotId,
        Func<string> allocateComponentId)
    {
        Dictionary<string, string> canonicalIdsByLocalId = new(StringComparer.Ordinal);
        List<DataModelOperation> resolved = new(operations.Count);

        foreach (DataModelOperation operation in operations)
        {
            switch (operation)
            {
                case AddSlot addSlot:
                    {
                        string canonicalSlotId = allocateSlotId();
                        if (!string.IsNullOrWhiteSpace(addSlot.Data.ID))
                        {
                            canonicalIdsByLocalId[addSlot.Data.ID] = canonicalSlotId;
                        }

                        resolved.Add(
                            new AddSlot
                            {
                                MessageID = addSlot.MessageID,
                                Data = new Slot
                                {
                                    ID = canonicalSlotId,
                                    Parent = addSlot.Data.Parent is null
                                        ? null
                                        : new Reference
                                        {
                                            TargetID = ResolveCanonicalId(addSlot.Data.Parent.TargetID, canonicalIdsByLocalId),
                                        },
                                    Name = addSlot.Data.Name,
                                    Position = addSlot.Data.Position,
                                    Rotation = addSlot.Data.Rotation,
                                },
                            });
                        break;
                    }
                case AddComponent addComponent:
                    {
                        string canonicalComponentId = allocateComponentId();
                        if (!string.IsNullOrWhiteSpace(addComponent.Data.ID))
                        {
                            canonicalIdsByLocalId[addComponent.Data.ID] = canonicalComponentId;
                        }

                        resolved.Add(
                            new AddComponent
                            {
                                MessageID = addComponent.MessageID,
                                ContainerSlotId = ResolveCanonicalId(addComponent.ContainerSlotId, canonicalIdsByLocalId),
                                Data = new Component
                                {
                                    ID = canonicalComponentId,
                                    ComponentType = addComponent.Data.ComponentType,
                                    Members = addComponent.Data.Members.ToDictionary(
                                        static pair => pair.Key,
                                        pair => CloneMemberWithResolvedReferences(pair.Value, canonicalIdsByLocalId),
                                        StringComparer.Ordinal),
                                },
                            });
                        break;
                    }
                case UpdateComponent updateComponent:
                    resolved.Add(new UpdateComponent
                    {
                        MessageID = updateComponent.MessageID,
                        Data = new Component
                        {
                            ID = ResolveCanonicalId(updateComponent.Data.ID, canonicalIdsByLocalId),
                            ComponentType = updateComponent.Data.ComponentType,
                            Members = updateComponent.Data.Members.ToDictionary(
                                static pair => pair.Key,
                                pair => CloneMemberWithResolvedReferences(pair.Value, canonicalIdsByLocalId),
                                StringComparer.Ordinal),
                        },
                    });
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported batch operation '{operation.GetType().Name}'.");
            }
        }

        return resolved;
    }

    private static async Task<BatchResponse> ExecuteBatchOperationsAsync(
        IReadOnlyList<DataModelOperation> operations,
        Func<string> allocateSlotId,
        Func<string> allocateComponentId,
        Func<AddSlot, CancellationToken, Task<string>> addSlotAsync,
        Func<AddComponent, CancellationToken, Task<string>> addComponentAsync,
        Func<UpdateComponent, CancellationToken, Task> updateComponentAsync,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DataModelOperation> resolvedOperations = ResolveBatchLocalSlotReferences(
            operations,
            allocateSlotId,
            allocateComponentId);
        List<Response> responses = [];
        foreach (DataModelOperation operation in resolvedOperations)
        {
            switch (operation)
            {
                case AddSlot addSlot:
                    {
                        string entityId = await addSlotAsync(addSlot, cancellationToken);
                        responses.Add(
                            new NewEntityId
                            {
                                Success = true,
                                SourceMessageID = addSlot.MessageID,
                                EntityId = entityId,
                            });
                        break;
                    }
                case AddComponent addComponent:
                    {
                        string entityId = await addComponentAsync(addComponent, cancellationToken);
                        responses.Add(
                            new NewEntityId
                            {
                                Success = true,
                                SourceMessageID = addComponent.MessageID,
                                EntityId = entityId,
                            });
                        break;
                    }
                case UpdateComponent updateComponent:
                    await updateComponentAsync(updateComponent, cancellationToken);
                    responses.Add(new Response
                    {
                        Success = true,
                        SourceMessageID = updateComponent.MessageID,
                    });
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported batch operation '{operation.GetType().Name}'.");
            }
        }

        return new BatchResponse
        {
            Success = true,
            Responses = responses,
        };
    }

    private static string ResolveCanonicalId(string targetId, IReadOnlyDictionary<string, string> canonicalIdsByLocalId)
    {
        return canonicalIdsByLocalId.TryGetValue(targetId, out string? canonicalId) ? canonicalId : targetId;
    }

    private static Member CloneMemberWithResolvedReferences(
        Member member,
        IReadOnlyDictionary<string, string> canonicalIdsByLocalId)
    {
        return member switch
        {
            Reference reference => new Reference
            {
                TargetID = ResolveCanonicalId(reference.TargetID, canonicalIdsByLocalId),
            },
            SyncList syncList => new SyncList
            {
                Elements = syncList.Elements
                    .Select(element => CloneMemberWithResolvedReferences(element, canonicalIdsByLocalId))
                    .ToList(),
            },
            EmptyElement => new EmptyElement(),
            Field_bool value => new Field_bool { Value = value.Value },
            Field_string value => new Field_string { Value = value.Value },
            Field_float value => new Field_float { Value = value.Value },
            Field_float2 value => new Field_float2 { Value = value.Value },
            Field_float3 value => new Field_float3 { Value = value.Value },
            Field_floatQ value => new Field_floatQ { Value = value.Value },
            Field_int2 value => new Field_int2 { Value = value.Value },
            Field_Enum value => new Field_Enum { Value = value.Value },
            Field_Nullable_Enum value => new Field_Nullable_Enum { Value = value.Value },
            Field_Uri value => new Field_Uri { Value = value.Value },
            Field_colorX value => new Field_colorX { Value = value.Value },
            _ => member,
        };
    }

    private sealed class FakeResoniteLinkClient : IResoniteLinkClient
    {
        private readonly FakeResoniteLinkSession session;

        public FakeResoniteLinkClient()
            : this(new FakeResoniteLinkSession())
        {
        }

        public FakeResoniteLinkClient(FakeResoniteLinkSession session)
        {
            this.session = session;
        }

        public int ConnectCallCount { get; private set; }

        public List<AddComponent> AddedComponents => session.AddedComponents;

        public List<AddSlot> AddedSlots => session.AddedSlots;

        public Dictionary<string, string> BuildingSlotIds => session.BuildingSlotIds;

        public List<ImportMeshRawData> ImportedMeshes => session.ImportedMeshes;

        public List<string> ImportedTexturePaths => session.ImportedTexturePaths;

        public List<ResoniteRawTextureImport> ImportedRawTextures => session.ImportedRawTextures;

        public List<ResoniteRawHdrTextureImport> ImportedRawHdrTextures => session.ImportedRawHdrTextures;

        public Dictionary<string, Component> ComponentsById => session.ComponentsById;

        public Dictionary<string, Slot> SlotsById => session.SlotsById;

        public Dictionary<string, string> SlotPaths => session.SlotPaths;

        public List<IReadOnlyList<DataModelOperation>> Batches => session.Batches;

        public int ImportedMeshCount { get; private set; }

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
            string createdSlotId = string.IsNullOrWhiteSpace(request.Data.ID)
                ? session.AllocateSlotId()
                : request.Data.ID;
            lock (session.Gate)
            {
                request.Data.ID = createdSlotId;
                session.SlotsById[createdSlotId] = request.Data;
                session.AddedSlots.Add(request);

                string? slotName = request.Data.Name?.Value;
                string slotPath = CreateSlotPath(session.SlotPaths, request.Data);
                session.SlotPaths[createdSlotId] = slotPath;

                if (!string.IsNullOrWhiteSpace(slotName)
                    && !slotPath.Contains("/Assets/", StringComparison.Ordinal)
                    && !string.Equals(slotPath, slotName, StringComparison.Ordinal)
                    && !slotName.All(char.IsAsciiDigit)
                    && !slotName.StartsWith("LOD", StringComparison.Ordinal)
                    && !string.Equals(slotName, "bldg", StringComparison.Ordinal)
                    && !string.Equals(slotName, "dem", StringComparison.Ordinal)
                    && !string.Equals(slotName, "tran", StringComparison.Ordinal)
                    && !string.Equals(slotName, "luse", StringComparison.Ordinal))
                {
                    session.BuildingSlotIds[slotName] = createdSlotId;
                }
            }

            return Task.FromResult(createdSlotId);
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                session.Batches.Add(operations.ToArray());
            }

            return ExecuteBatchOperationsAsync(
                operations,
                session.AllocateSlotId,
                session.AllocateComponentId,
                AddSlotAsync,
                AddComponentAsync,
                UpdateComponentAsync,
                cancellationToken);
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
            lock (session.Gate)
            {
                if (string.Equals(slotId, "Root", StringComparison.Ordinal))
                {
                    return Task.FromResult<Slot?>(CreateSyntheticRootSlot(session, depth, CloneSlot));
                }

                session.SlotsById.TryGetValue(slotId, out Slot? slot);
                return Task.FromResult(slot is null ? null : CloneSlot(slot, depth));
            }
        }

        public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                session.ImportedMeshes.Add(request);
                ImportedMeshCount++;
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
                Component existing = session.ComponentsById[request.Data.ID];
                foreach ((string memberName, Member member) in request.Data.Members)
                {
                    existing.Members[memberName] = member;
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
    }

    private sealed class VisibilityLagClient(VisibilityLagSession session) : IResoniteLinkClient
    {
        private readonly int clientId = session.RegisterClient();

        public void Dispose()
        {
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                if (!session.IsSlotVisible(clientId, request.ContainerSlotId))
                {
                    session.RejectedInvisibleParentMutationCount++;
                    throw new InvalidOperationException($"Container slot '{request.ContainerSlotId}' is not visible on client {clientId}.");
                }

                string createdComponentId = session.AllocateComponentId();
                request.Data.ID = createdComponentId;
                session.ComponentsById[createdComponentId] = request.Data;
                if (session.SlotsById.TryGetValue(request.ContainerSlotId, out Slot? containerSlot))
                {
                    containerSlot.Components ??= [];
                    containerSlot.Components.Add(request.Data);
                }
                session.MarkComponentVisible(clientId, createdComponentId);
                return Task.FromResult(createdComponentId);
            }
        }

        public Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                string parentId = request.Data.Parent?.TargetID ?? "Root";
                if (!string.Equals(parentId, "Root", StringComparison.Ordinal)
                    && !session.IsSlotVisible(clientId, parentId))
                {
                    session.RejectedInvisibleParentMutationCount++;
                    throw new InvalidOperationException($"Parent slot '{parentId}' is not visible on client {clientId}.");
                }

                string createdSlotId = string.IsNullOrWhiteSpace(request.Data.ID)
                    ? session.AllocateSlotId()
                    : request.Data.ID;
                request.Data.ID = createdSlotId;
                session.SlotsById[createdSlotId] = request.Data;
                session.MarkSlotVisible(clientId, createdSlotId);
                return Task.FromResult(createdSlotId);
            }
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            return ExecuteBatchOperationsAsync(
                operations,
                session.AllocateSlotId,
                session.AllocateComponentId,
                AddSlotAsync,
                AddComponentAsync,
                UpdateComponentAsync,
                cancellationToken);
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                if (!session.IsComponentVisible(clientId, componentId))
                {
                    if (session.ComponentsById.ContainsKey(componentId))
                    {
                        session.DelayedVisibilityProbeCount++;
                        session.MarkComponentVisible(clientId, componentId);
                    }

                    return Task.FromResult<Component?>(null);
                }

                session.ComponentsById.TryGetValue(componentId, out Component? component);
                return Task.FromResult(component);
            }
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                if (string.Equals(slotId, "Root", StringComparison.Ordinal))
                {
                    return Task.FromResult<Slot?>(CreateVisibilityLagRootSlot(depth));
                }

                if (!session.IsSlotVisible(clientId, slotId))
                {
                    if (session.SlotsById.ContainsKey(slotId))
                    {
                        session.DelayedVisibilityProbeCount++;
                        session.MarkSlotVisible(clientId, slotId);
                    }

                    return Task.FromResult<Slot?>(null);
                }

                session.SlotsById.TryGetValue(slotId, out Slot? slot);
                return Task.FromResult(slot is null ? null : CloneSlot(slot, depth));
            }
        }

        public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                session.ImportedMeshCount++;
                return Task.FromResult(new Uri($"resdb:///mesh/{session.ImportedMeshCount}", UriKind.Absolute));
            }
        }

        public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                session.ImportedTextureCount++;
                return Task.FromResult(new Uri($"resdb:///texture/{session.ImportedTextureCount}", UriKind.Absolute));
            }
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                if (!session.ComponentsById.TryGetValue(request.Data.ID, out Component? existing))
                {
                    return Task.CompletedTask;
                }

                foreach ((string memberName, Member member) in request.Data.Members)
                {
                    existing.Members[memberName] = member;
                }

                return Task.CompletedTask;
            }
        }

        private Slot CloneSlot(Slot source, int depth)
        {
            Slot clone = new()
            {
                ID = source.ID,
                Parent = source.Parent,
                Name = source.Name,
                Position = source.Position,
                Rotation = source.Rotation,
                Components = source.Components,
            };

            if (depth <= 0)
            {
                return clone;
            }

            clone.Children = session.SlotsById.Values
                .Where(slot =>
                    string.Equals(slot.Parent?.TargetID, source.ID, StringComparison.Ordinal)
                    && session.IsSlotVisible(clientId, slot.ID))
                .Select(slot => CloneSlot(slot, depth - 1))
                .ToList();
            return clone;
        }

        private Slot CreateVisibilityLagRootSlot(int depth)
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

            root.Children = session.SlotsById.Values
                .Where(slot =>
                    string.Equals(slot.Parent?.TargetID, "Root", StringComparison.Ordinal)
                    && session.IsSlotVisible(clientId, slot.ID))
                .Select(slot => CloneSlot(slot, depth - 1))
                .ToList();
            return root;
        }
    }

    private sealed class GlobalLocalIdRejectingBatchClient(
        FakeResoniteLinkSession session,
        HashSet<string> claimedLocalIds) : IResoniteLinkClient
    {
        public void Dispose()
        {
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            string createdSlotId = string.IsNullOrWhiteSpace(request.Data.ID)
                ? session.AllocateSlotId()
                : request.Data.ID;
            lock (session.Gate)
            {
                request.Data.ID = createdSlotId;
                session.SlotsById[createdSlotId] = request.Data;
                session.AddedSlots.Add(request);
                session.SlotPaths[createdSlotId] = CreateSlotPath(session.SlotPaths, request.Data);
            }

            return Task.FromResult(createdSlotId);
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                foreach (string localId in operations
                    .OfType<AddSlot>()
                    .Select(static addSlot => addSlot.Data.ID)
                    .Concat(operations.OfType<AddComponent>().Select(static addComponent => addComponent.Data.ID))
                    .Where(static id => !string.IsNullOrWhiteSpace(id)))
                {
                    if (!claimedLocalIds.Add(localId))
                    {
                        throw new InvalidOperationException($"ResoniteLink validate slot failed: ID '{localId}' is already in use");
                    }
                }

                session.Batches.Add(operations.ToArray());
            }

            return ExecuteBatchOperationsAsync(
                operations,
                session.AllocateSlotId,
                session.AllocateComponentId,
                AddSlotAsync,
                AddComponentAsync,
                UpdateComponentAsync,
                cancellationToken);
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                session.ComponentsById.TryGetValue(componentId, out Component? component);
                return Task.FromResult(component);
            }
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                if (string.Equals(slotId, "Root", StringComparison.Ordinal))
                {
                    return Task.FromResult<Slot?>(CreateSyntheticRootSlot(session, depth, CloneSlot));
                }

                session.SlotsById.TryGetValue(slotId, out Slot? slot);
                return Task.FromResult(slot is null ? null : CloneSlot(slot, depth));
            }
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
                return Task.FromResult(new Uri($"resdb:///texture/{session.ImportedTexturePaths.Count + session.ImportedRawTextures.Count + session.ImportedRawHdrTextures.Count}", UriKind.Absolute));
            }
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                Component existing = session.ComponentsById[request.Data.ID];
                foreach ((string memberName, Member member) in request.Data.Members)
                {
                    existing.Members[memberName] = member;
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
            };

            if (depth <= 0)
            {
                return clone;
            }

            clone.Children = session.SlotsById.Values
                .Where(slot => string.Equals(slot.Parent?.TargetID, source.ID, StringComparison.Ordinal))
                .Select(slot => CloneSlot(slot, depth - 1))
                .ToList();
            return clone;
        }
    }

    private sealed class ResponseAuthoritativeIdClient(
        FakeResoniteLinkSession session,
        bool injectPreexistingPresentationSibling = false,
        string? failedBatchComponentType = null) : IResoniteLinkClient
    {
        private int nextResponseSlotId;
        private int nextResponseComponentId;
        private int remainingFailedBatchComponentResponses = string.IsNullOrWhiteSpace(failedBatchComponentType) ? 0 : 1;

        public List<string> ReturnedSlotResponseIds { get; } = [];

        public List<string> ReturnedComponentResponseIds { get; } = [];

        public int RejectedAliasMutationCount { get; private set; }

        public int BatchMutationCount { get; private set; }

        public bool PreexistingPresentationSiblingInjected { get; private set; }

        public int FailedBatchResponseCount { get; private set; }

        public void Dispose()
        {
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                if (!string.Equals(request.ContainerSlotId, "Root", StringComparison.Ordinal)
                    && !session.SlotsById.TryGetValue(request.ContainerSlotId, out Slot? containerSlot))
                {
                    RejectedAliasMutationCount++;
                    throw new InvalidOperationException($"Container slot '{request.ContainerSlotId}' is not a canonical slot ID.");
                }

                string responseComponentId = string.Create(
                    CultureInfo.InvariantCulture,
                    $"resp_component_{Interlocked.Increment(ref nextResponseComponentId)}");
                ReturnedComponentResponseIds.Add(responseComponentId);
                session.AddedComponents.Add(CloneAddComponent(request));

                Component createdComponent = new()
                {
                    ID = responseComponentId,
                    ComponentType = request.Data.ComponentType,
                    Members = new Dictionary<string, Member>(request.Data.Members, StringComparer.Ordinal),
                };
                session.ComponentsById[responseComponentId] = createdComponent;
                if (session.SlotsById.TryGetValue(request.ContainerSlotId, out Slot? resolvedContainerSlot))
                {
                    resolvedContainerSlot.Components ??= [];
                    resolvedContainerSlot.Components.Add(createdComponent);
                }

                return Task.FromResult(responseComponentId);
            }
        }

        public Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                string parentId = request.Data.Parent?.TargetID ?? "Root";
                if (!string.Equals(parentId, "Root", StringComparison.Ordinal)
                    && !session.SlotsById.ContainsKey(parentId))
                {
                    RejectedAliasMutationCount++;
                    throw new InvalidOperationException($"Parent slot '{parentId}' is not a canonical slot ID.");
                }

                string responseSlotId = string.Create(
                    CultureInfo.InvariantCulture,
                    $"resp_slot_{Interlocked.Increment(ref nextResponseSlotId)}");
                ReturnedSlotResponseIds.Add(responseSlotId);
                session.AddedSlots.Add(CloneAddSlot(request));

                Slot createdSlot = new()
                {
                    ID = responseSlotId,
                    Parent = request.Data.Parent is null
                        ? null
                        : new Reference
                        {
                            TargetID = parentId,
                        },
                    Name = request.Data.Name,
                    Position = request.Data.Position,
                    Rotation = request.Data.Rotation,
                };
                session.SlotsById[responseSlotId] = createdSlot;
                session.SlotPaths[responseSlotId] = CreateSlotPath(session.SlotPaths, createdSlot);
                TrackBuildingSlotId(session, responseSlotId, createdSlot);
                MaybeInjectPreexistingPresentationSibling(responseSlotId, createdSlot);
                return Task.FromResult(responseSlotId);
            }
        }

        public async Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BatchMutationCount++;
            Dictionary<string, string> canonicalIdsByLocalId = new(StringComparer.Ordinal);
            List<Response> responses = [];
            foreach (DataModelOperation operation in operations)
            {
                switch (operation)
                {
                    case AddSlot addSlot:
                        {
                            AddSlot resolvedAddSlot = CloneAddSlot(addSlot);
                            if (resolvedAddSlot.Data.Parent is not null
                                && canonicalIdsByLocalId.TryGetValue(resolvedAddSlot.Data.Parent.TargetID, out string? canonicalParentId))
                            {
                                resolvedAddSlot.Data.Parent = new Reference
                                {
                                    TargetID = canonicalParentId,
                                };
                            }

                            string createdSlotId = await AddSlotAsync(resolvedAddSlot, cancellationToken);
                            if (!string.IsNullOrWhiteSpace(addSlot.Data.ID))
                            {
                                canonicalIdsByLocalId[addSlot.Data.ID] = createdSlotId;
                            }

                            responses.Add(new NewEntityId
                            {
                                Success = true,
                                SourceMessageID = addSlot.MessageID,
                                EntityId = createdSlotId,
                            });
                            break;
                        }
                    case AddComponent addComponent:
                        {
                            AddComponent resolvedAddComponent = CloneAddComponent(addComponent);
                            if (canonicalIdsByLocalId.TryGetValue(resolvedAddComponent.ContainerSlotId, out string? canonicalContainerId))
                            {
                                resolvedAddComponent.ContainerSlotId = canonicalContainerId;
                            }

                            resolvedAddComponent.Data.Members = resolvedAddComponent.Data.Members.ToDictionary(
                                static pair => pair.Key,
                                pair => ResolveBatchMember(pair.Value, canonicalIdsByLocalId),
                                StringComparer.Ordinal);
                            string createdComponentId = await AddComponentAsync(resolvedAddComponent, cancellationToken);
                            if (!string.IsNullOrWhiteSpace(addComponent.Data.ID))
                            {
                                canonicalIdsByLocalId[addComponent.Data.ID] = createdComponentId;
                            }

                            if (remainingFailedBatchComponentResponses > 0
                                && string.Equals(
                                    addComponent.Data.ComponentType,
                                    failedBatchComponentType,
                                    StringComparison.Ordinal))
                            {
                                remainingFailedBatchComponentResponses--;
                                FailedBatchResponseCount++;
                                responses.Add(new Response
                                {
                                    Success = false,
                                    ErrorInfo = "Simulated batch component failure.",
                                    SourceMessageID = addComponent.MessageID,
                                });
                                break;
                            }

                            responses.Add(new NewEntityId
                            {
                                Success = true,
                                SourceMessageID = addComponent.MessageID,
                                EntityId = createdComponentId,
                            });
                            break;
                        }
                    case UpdateComponent updateComponent:
                        await UpdateComponentAsync(updateComponent, cancellationToken);
                        responses.Add(new Response
                        {
                            Success = true,
                            SourceMessageID = updateComponent.MessageID,
                        });
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported batch operation '{operation.GetType().Name}'.");
                }
            }

            return new BatchResponse
            {
                Success = true,
                Responses = responses,
            };
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                session.ComponentsById.TryGetValue(componentId, out Component? component);
                return Task.FromResult(component);
            }
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                if (string.Equals(slotId, "Root", StringComparison.Ordinal))
                {
                    return Task.FromResult<Slot?>(CreateSyntheticRootSlot(session, depth, CloneSlot));
                }

                session.SlotsById.TryGetValue(slotId, out Slot? slot);
                return Task.FromResult(slot is null ? null : CloneSlot(slot, depth));
            }
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
                if (!session.ComponentsById.TryGetValue(request.Data.ID, out Component? existing))
                {
                    RejectedAliasMutationCount++;
                    throw new InvalidOperationException($"Component '{request.Data.ID}' is not a canonical component ID.");
                }

                foreach ((string memberName, Member member) in request.Data.Members)
                {
                    existing.Members[memberName] = member;
                }
            }

            return Task.CompletedTask;
        }

        private void MaybeInjectPreexistingPresentationSibling(string slotId, Slot slot)
        {
            if (!injectPreexistingPresentationSibling
                || PreexistingPresentationSiblingInjected
                || !string.Equals(slot.Name?.Value, "LOD2", StringComparison.Ordinal)
                || !session.SlotPaths.TryGetValue(slotId, out string? slotPath)
                || slotPath.Contains("/Assets/", StringComparison.Ordinal))
            {
                return;
            }

            string foreignSlotId = session.AllocateSlotId();
            Slot foreignSlot = new()
            {
                ID = foreignSlotId,
                Parent = new Reference
                {
                    TargetID = slotId,
                },
                Name = new Field_string
                {
                    Value = "Building One",
                },
            };
            session.SlotsById[foreignSlotId] = foreignSlot;
            session.SlotPaths[foreignSlotId] = CreateSlotPath(session.SlotPaths, foreignSlot);
            PreexistingPresentationSiblingInjected = true;
        }

        private static AddSlot CloneAddSlot(AddSlot request)
        {
            return new AddSlot
            {
                Data = new Slot
                {
                    ID = request.Data.ID,
                    Parent = request.Data.Parent is null
                        ? null
                        : new Reference
                        {
                            TargetID = request.Data.Parent.TargetID,
                        },
                    Name = request.Data.Name,
                    Position = request.Data.Position,
                    Rotation = request.Data.Rotation,
                },
            };
        }

        private static AddComponent CloneAddComponent(AddComponent request)
        {
            return new AddComponent
            {
                ContainerSlotId = request.ContainerSlotId,
                Data = new Component
                {
                    ID = request.Data.ID,
                    ComponentType = request.Data.ComponentType,
                    Members = new Dictionary<string, Member>(request.Data.Members, StringComparer.Ordinal),
                },
            };
        }

        private static Member ResolveBatchMember(
            Member member,
            IReadOnlyDictionary<string, string> canonicalIdsByLocalId)
        {
            return member switch
            {
                Reference reference => new Reference
                {
                    TargetID = canonicalIdsByLocalId.TryGetValue(reference.TargetID, out string? canonicalId)
                        ? canonicalId
                        : reference.TargetID,
                },
                SyncList syncList => new SyncList
                {
                    Elements = syncList.Elements
                        .Select(element => ResolveBatchMember(element, canonicalIdsByLocalId))
                        .ToList(),
                },
                _ => member,
            };
        }

        private static void TrackBuildingSlotId(FakeResoniteLinkSession session, string actualSlotId, Slot actualSlot)
        {
            string? slotName = actualSlot.Name?.Value;
            string slotPath = session.SlotPaths[actualSlotId];
            if (!string.IsNullOrWhiteSpace(slotName)
                && !slotPath.Contains("/Assets/", StringComparison.Ordinal)
                && !string.Equals(slotPath, slotName, StringComparison.Ordinal)
                && !slotName.All(char.IsAsciiDigit)
                && !slotName.StartsWith("LOD", StringComparison.Ordinal)
                && !string.Equals(slotName, "bldg", StringComparison.Ordinal)
                && !string.Equals(slotName, "dem", StringComparison.Ordinal)
                && !string.Equals(slotName, "tran", StringComparison.Ordinal)
                && !string.Equals(slotName, "luse", StringComparison.Ordinal))
            {
                session.BuildingSlotIds[slotName] = actualSlotId;
            }
        }

        private Slot CloneSlot(Slot source, int depth)
        {
            Slot clone = new()
            {
                ID = source.ID,
                Parent = source.Parent,
                Name = source.Name,
                Position = source.Position,
                Rotation = source.Rotation,
                Components = source.Components,
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
    }

    private sealed class SessionScopedMutationClient(SessionScopedMutationSession session) : IResoniteLinkClient
    {
        private readonly int clientId = session.RegisterClient();

        public int ImportedMeshCount { get; private set; }

        public void Dispose()
        {
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                if (!session.IsSlotOwnedByClient(request.ContainerSlotId, clientId))
                {
                    session.ForbiddenForeignMutationCount++;
                    throw new InvalidOperationException($"Container slot '{request.ContainerSlotId}' is not owned by client {clientId}.");
                }

                string createdComponentId = session.AllocateComponentId();
                request.Data.ID = createdComponentId;
                session.ComponentsById[createdComponentId] = request.Data;
                if (session.SlotsById.TryGetValue(request.ContainerSlotId, out Slot? containerSlot))
                {
                    containerSlot.Components ??= [];
                    containerSlot.Components.Add(request.Data);
                }
                session.ComponentOwners[createdComponentId] = clientId;
                return Task.FromResult(createdComponentId);
            }
        }

        public Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                string parentId = request.Data.Parent?.TargetID ?? "Root";
                if (!string.Equals(parentId, "Root", StringComparison.Ordinal)
                    && !session.IsSlotOwnedByClient(parentId, clientId))
                {
                    session.ForbiddenForeignMutationCount++;
                    throw new InvalidOperationException($"Parent slot '{parentId}' is not owned by client {clientId}.");
                }

                string createdSlotId = string.IsNullOrWhiteSpace(request.Data.ID)
                    ? session.AllocateSlotId()
                    : request.Data.ID;
                request.Data.ID = createdSlotId;
                session.SlotsById[createdSlotId] = request.Data;
                session.SlotOwners[createdSlotId] = clientId;
                return Task.FromResult(createdSlotId);
            }
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            return ExecuteBatchOperationsAsync(
                operations,
                session.AllocateSlotId,
                session.AllocateComponentId,
                AddSlotAsync,
                AddComponentAsync,
                UpdateComponentAsync,
                cancellationToken);
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                if (!session.ComponentOwners.TryGetValue(componentId, out int ownerClientId)
                    || ownerClientId != clientId)
                {
                    return Task.FromResult<Component?>(null);
                }

                session.ComponentsById.TryGetValue(componentId, out Component? component);
                return Task.FromResult(component);
            }
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                if (string.Equals(slotId, "Root", StringComparison.Ordinal))
                {
                    return Task.FromResult<Slot?>(CreateSyntheticRootSlot(session, depth, CloneSlot));
                }

                if (!session.SlotOwners.TryGetValue(slotId, out int ownerClientId)
                    || ownerClientId != clientId)
                {
                    return Task.FromResult<Slot?>(null);
                }

                session.SlotsById.TryGetValue(slotId, out Slot? slot);
                return Task.FromResult(slot is null ? null : CloneSlot(slot, depth));
            }
        }

        public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                session.ImportedMeshCount++;
                ImportedMeshCount++;
                return Task.FromResult(new Uri($"resdb:///mesh/{session.ImportedMeshCount}", UriKind.Absolute));
            }
        }

        public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                session.ImportedTextureCount++;
                return Task.FromResult(new Uri($"resdb:///texture/{session.ImportedTextureCount}", UriKind.Absolute));
            }
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                if (!session.ComponentOwners.TryGetValue(request.Data.ID, out int ownerClientId)
                    || ownerClientId != clientId
                    || !session.ComponentsById.TryGetValue(request.Data.ID, out Component? existing))
                {
                    session.ForbiddenForeignMutationCount++;
                    throw new InvalidOperationException($"Component '{request.Data.ID}' is not owned by client {clientId}.");
                }

                foreach ((string memberName, Member member) in request.Data.Members)
                {
                    existing.Members[memberName] = member;
                }

                return Task.CompletedTask;
            }
        }

        private Slot CloneSlot(Slot source, int depth)
        {
            Slot clone = new()
            {
                ID = source.ID,
                Parent = source.Parent,
                Name = source.Name,
                Position = source.Position,
                Rotation = source.Rotation,
                Components = source.Components,
            };

            if (depth <= 0)
            {
                return clone;
            }

            clone.Children = session.SlotsById.Values
                .Where(slot =>
                    string.Equals(slot.Parent?.TargetID, source.ID, StringComparison.Ordinal)
                    && session.IsSlotOwnedByClient(slot.ID, clientId))
                .Select(slot => CloneSlot(slot, depth - 1))
                .ToList();
            return clone;
        }
    }

    private sealed class SessionScopedMutationSession
    {
        private int nextClientId;
        private int nextSlotId;
        private int nextComponentId;

        public object Gate { get; } = new();

        public Dictionary<string, Slot> SlotsById { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> SlotOwners { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Component> ComponentsById { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> ComponentOwners { get; } = new(StringComparer.Ordinal);

        public int ImportedMeshCount { get; set; }

        public int ImportedTextureCount { get; set; }

        public int ForbiddenForeignMutationCount { get; set; }

        public int RegisterClient() => Interlocked.Increment(ref nextClientId);

        public string AllocateSlotId() => string.Create(CultureInfo.InvariantCulture, $"srv_slot_{Interlocked.Increment(ref nextSlotId)}");

        public string AllocateComponentId() => string.Create(CultureInfo.InvariantCulture, $"srv_component_{Interlocked.Increment(ref nextComponentId)}");

        public bool IsSlotOwnedByClient(string slotId, int clientId)
        {
            return SlotOwners.TryGetValue(slotId, out int ownerClientId) && ownerClientId == clientId;
        }
    }

    private sealed class VisibilityLagSession
    {
        private int nextClientId;
        private int nextSlotId;
        private int nextComponentId;
        private readonly Dictionary<int, HashSet<string>> visibleSlotsByClient = [];
        private readonly Dictionary<int, HashSet<string>> visibleComponentsByClient = [];

        public object Gate { get; } = new();
        public Dictionary<string, Slot> SlotsById { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Component> ComponentsById { get; } = new(StringComparer.Ordinal);
        public int ImportedMeshCount { get; set; }
        public int ImportedTextureCount { get; set; }
        public int DelayedVisibilityProbeCount { get; set; }
        public int RejectedInvisibleParentMutationCount { get; set; }

        public int RegisterClient()
        {
            lock (Gate)
            {
                int clientId = ++nextClientId;
                visibleSlotsByClient[clientId] = ["Root"];
                visibleComponentsByClient[clientId] = [];
                return clientId;
            }
        }

        public bool IsSlotVisible(int clientId, string slotId)
        {
            return visibleSlotsByClient[clientId].Contains(slotId);
        }

        public void MarkSlotVisible(int clientId, string slotId)
        {
            visibleSlotsByClient[clientId].Add(slotId);
        }

        public bool IsComponentVisible(int clientId, string componentId)
        {
            return visibleComponentsByClient[clientId].Contains(componentId);
        }

        public void MarkComponentVisible(int clientId, string componentId)
        {
            visibleComponentsByClient[clientId].Add(componentId);
        }

        public string AllocateSlotId()
        {
            return $"lag_slot_{Interlocked.Increment(ref nextSlotId)}";
        }

        public string AllocateComponentId()
        {
            return $"lag_component_{Interlocked.Increment(ref nextComponentId)}";
        }
    }

    private sealed class FailingImportMeshSharedClient(
        FakeResoniteLinkSession session,
        Func<bool> shouldFailImportMesh) : IResoniteLinkClient
    {
        public void Dispose()
        {
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            string createdSlotId = string.IsNullOrWhiteSpace(request.Data.ID)
                ? session.AllocateSlotId()
                : request.Data.ID;
            lock (session.Gate)
            {
                request.Data.ID = createdSlotId;
                session.SlotsById[createdSlotId] = request.Data;
                session.AddedSlots.Add(request);
                session.SlotPaths[createdSlotId] = CreateSlotPath(session.SlotPaths, request.Data);
            }

            return Task.FromResult(createdSlotId);
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                session.Batches.Add(operations.ToArray());
            }

            return ExecuteBatchOperationsAsync(
                operations,
                session.AllocateSlotId,
                session.AllocateComponentId,
                AddSlotAsync,
                AddComponentAsync,
                UpdateComponentAsync,
                cancellationToken);
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
            if (shouldFailImportMesh())
            {
                throw new InvalidOperationException("Simulated mesh import failure.");
            }

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
                }

                return Task.FromResult(new Uri($"resdb:///texture/{session.ImportedTexturePaths.Count}", UriKind.Absolute));
            }
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                if (!session.ComponentsById.TryGetValue(request.Data.ID, out Component? existing))
                {
                    existing = new Component
                    {
                        ID = request.Data.ID,
                        Members = new Dictionary<string, Member>(StringComparer.Ordinal),
                    };
                    session.ComponentsById[request.Data.ID] = existing;
                }

                foreach ((string memberName, Member member) in request.Data.Members)
                {
                    existing.Members[memberName] = member;
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
    }

    private sealed class LaggyExistingCompletionMeshRootClient(
        FakeResoniteLinkSession session,
        string hiddenMeshCode,
        int hiddenPollCount) : IResoniteLinkClient
    {
        private int remainingHiddenPolls = hiddenPollCount;

        public void Dispose()
        {
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            string createdSlotId = string.IsNullOrWhiteSpace(request.Data.ID)
                ? session.AllocateSlotId()
                : request.Data.ID;
            lock (session.Gate)
            {
                request.Data.ID = createdSlotId;
                session.SlotsById[createdSlotId] = request.Data;
                session.AddedSlots.Add(request);
                session.SlotPaths[createdSlotId] = CreateSlotPath(session.SlotPaths, request.Data);
            }

            return Task.FromResult(createdSlotId);
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                session.Batches.Add(operations.ToArray());
            }

            return ExecuteBatchOperationsAsync(
                operations,
                session.AllocateSlotId,
                session.AllocateComponentId,
                AddSlotAsync,
                AddComponentAsync,
                UpdateComponentAsync,
                cancellationToken);
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                session.ComponentsById.TryGetValue(componentId, out Component? component);
                return Task.FromResult(component);
            }
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                if (string.Equals(slotId, "Root", StringComparison.Ordinal))
                {
                    return Task.FromResult<Slot?>(CreateSyntheticRootSlot(session, depth, CloneSlot));
                }

                session.SlotsById.TryGetValue(slotId, out Slot? slot);
                if (slot is null)
                {
                    return Task.FromResult<Slot?>(null);
                }

                Slot clone = CloneSlot(slot, depth);
                if (depth > 0
                    && remainingHiddenPolls > 0
                    && string.Equals(clone.Name?.Value, "PLATEAU tokyo23ku", StringComparison.Ordinal)
                    && clone.Children is not null)
                {
                    clone.Children = clone.Children
                        .Where(child => !string.Equals(child.Name?.Value, hiddenMeshCode, StringComparison.Ordinal))
                        .ToList();
                    remainingHiddenPolls--;
                }

                return Task.FromResult<Slot?>(clone);
            }
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
                }

                return Task.FromResult(new Uri($"resdb:///texture/{session.ImportedTexturePaths.Count}", UriKind.Absolute));
            }
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                if (session.ComponentsById.TryGetValue(request.Data.ID, out Component? existing))
                {
                    foreach ((string memberName, Member member) in request.Data.Members)
                    {
                        existing.Members[memberName] = member;
                    }
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
    }

    private sealed class StubTerrainTextureAssetGenerator : ITerrainTextureAssetGenerator
    {
        public List<TerrainTextureOverlay> RequestedOverlays { get; } = [];

        public Task<ResoniteRawTextureImport> EnsureTextureAsync(
            TerrainTextureOverlay terrainTextureOverlay,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedOverlays.Add(terrainTextureOverlay);
            using Image<Rgba32> image = new(2, 2, new Rgba32(255, 255, 255, 255));
            byte[] rawBytes = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(rawBytes);
            return Task.FromResult(
                new ResoniteRawTextureImport(
                    image.Width,
                    image.Height,
                    "sRGB",
                    rawBytes,
                    terrainTextureOverlay.TexturePath));
        }
    }

    private sealed class BlockingResoniteLinkClient : IResoniteLinkClient
    {
        private readonly Dictionary<string, Component> componentsById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Slot> slotsById = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource meshImportRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int nextComponentId;
        private int nextSlotId;

        public List<IReadOnlyList<DataModelOperation>> Batches { get; } = [];

        public void Dispose()
        {
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string createdComponentId = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"srv_component_{Interlocked.Increment(ref nextComponentId)}");
            request.Data.ID = createdComponentId;
            componentsById[createdComponentId] = request.Data;
            if (slotsById.TryGetValue(request.ContainerSlotId, out Slot? containerSlot))
            {
                containerSlot.Components ??= [];
                containerSlot.Components.Add(request.Data);
            }

            return Task.FromResult(createdComponentId);
        }

        public Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string createdSlotId = string.IsNullOrWhiteSpace(request.Data.ID)
                ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"srv_slot_{Interlocked.Increment(ref nextSlotId)}")
                : request.Data.ID;
            request.Data.ID = createdSlotId;
            slotsById[createdSlotId] = request.Data;
            return Task.FromResult(createdSlotId);
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Batches.Add(operations.ToArray());
            return ExecuteBatchOperationsAsync(
                operations,
                () => string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"srv_slot_{Interlocked.Increment(ref nextSlotId)}"),
                () => string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"srv_component_{Interlocked.Increment(ref nextComponentId)}"),
                AddSlotAsync,
                AddComponentAsync,
                UpdateComponentAsync,
                cancellationToken);
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            componentsById.TryGetValue(componentId, out Component? component);
            return Task.FromResult(component);
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(slotId, "Root", StringComparison.Ordinal))
            {
                return Task.FromResult<Slot?>(new Slot
                {
                    ID = "Root",
                    Name = new Field_string
                    {
                        Value = "Root",
                    },
                });
            }

            slotsById.TryGetValue(slotId, out Slot? slot);
            return Task.FromResult(slot);
        }

        public async Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            await meshImportRelease.Task.WaitAsync(cancellationToken);
            return new Uri("resdb:///mesh/0", UriKind.Absolute);
        }

        public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new Uri("resdb:///texture/0", UriKind.Absolute));
        }

        public void ReleaseMeshImports()
        {
            meshImportRelease.TrySetResult();
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class DelayedConnectClient : IResoniteLinkClient
    {
        private readonly TaskCompletionSource allowConnect = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ConnectCallCount { get; private set; }

        public TaskCompletionSource ConnectStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose()
        {
        }

        public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectCallCount++;
            ConnectStarted.TrySetResult();
            await allowConnect.Task.WaitAsync(cancellationToken);
        }

        public void AllowConnect()
        {
            allowConnect.TrySetResult();
        }

        public Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(request.Data.ID);
        }

        public Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(request.Data.ID);
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new BatchResponse
            {
                Success = true,
                Responses = [],
            });
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Component?>(null);
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Slot?>(null);
        }

        public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new Uri("resdb:///mesh/0", UriKind.Absolute));
        }

        public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new Uri("resdb:///texture/0", UriKind.Absolute));
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

    }

    private sealed class UncooperativeConnectClient : IResoniteLinkClient
    {
        private readonly TaskCompletionSource connectNeverCompletes = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCallCount { get; private set; }

        public TaskCompletionSource ConnectStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose()
        {
            DisposeCallCount++;
        }

        public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectStarted.TrySetResult();
            await connectNeverCompletes.Task;
        }

        public Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(request.Data.ID);
        }

        public Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(request.Data.ID);
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new BatchResponse
            {
                Success = true,
                Responses = [],
            });
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Component?>(null);
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Slot?>(null);
        }

        public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new Uri("resdb:///mesh/0", UriKind.Absolute));
        }

        public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new Uri("resdb:///texture/0", UriKind.Absolute));
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class CancellationAwareConnectClient : IResoniteLinkClient
    {
        public TaskCompletionSource ConnectStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ConnectCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose()
        {
        }

        public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            ConnectStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ConnectCanceled.TrySetResult();
                throw;
            }
        }

        public Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(request.Data.ID);
        }

        public Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(request.Data.ID);
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new BatchResponse
            {
                Success = true,
                Responses = [],
            });
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Component?>(null);
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Slot?>(null);
        }

        public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new Uri("resdb:///mesh/0", UriKind.Absolute));
        }

        public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new Uri("resdb:///texture/0", UriKind.Absolute));
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class NonCancelableBlockingResoniteLinkClient : IResoniteLinkClient
    {
        private readonly Dictionary<string, Component> componentsById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Slot> slotsById = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource meshImportNeverCompletes = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int nextComponentId;
        private int nextSlotId;

        public int DisposeCallCount { get; private set; }

        public TaskCompletionSource ImportStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose()
        {
            DisposeCallCount++;
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string createdComponentId = string.Create(CultureInfo.InvariantCulture, $"srv_component_{Interlocked.Increment(ref nextComponentId)}");
            request.Data.ID = createdComponentId;
            componentsById[createdComponentId] = request.Data;
            if (slotsById.TryGetValue(request.ContainerSlotId, out Slot? containerSlot))
            {
                containerSlot.Components ??= [];
                containerSlot.Components.Add(request.Data);
            }

            return Task.FromResult(createdComponentId);
        }

        public Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string createdSlotId = string.IsNullOrWhiteSpace(request.Data.ID)
                ? string.Create(CultureInfo.InvariantCulture, $"srv_slot_{Interlocked.Increment(ref nextSlotId)}")
                : request.Data.ID;
            request.Data.ID = createdSlotId;
            slotsById[createdSlotId] = request.Data;
            return Task.FromResult(createdSlotId);
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ExecuteBatchOperationsAsync(
                operations,
                () => string.Create(CultureInfo.InvariantCulture, $"srv_slot_{Interlocked.Increment(ref nextSlotId)}"),
                () => string.Create(CultureInfo.InvariantCulture, $"srv_component_{Interlocked.Increment(ref nextComponentId)}"),
                AddSlotAsync,
                AddComponentAsync,
                UpdateComponentAsync,
                cancellationToken);
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            componentsById.TryGetValue(componentId, out Component? component);
            return Task.FromResult(component);
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(slotId, "Root", StringComparison.Ordinal))
            {
                Slot root = new()
                {
                    ID = "Root",
                    Name = new Field_string
                    {
                        Value = "Root",
                    },
                };
                if (depth > 0)
                {
                    root.Children = slotsById.Values
                        .Where(static slot => string.Equals(slot.Parent?.TargetID, "Root", StringComparison.Ordinal))
                        .Select(slot => CloneSlot(slot, depth - 1))
                        .ToList();
                }

                return Task.FromResult<Slot?>(root);
            }

            slotsById.TryGetValue(slotId, out Slot? slot);
            return Task.FromResult(slot is null ? null : CloneSlot(slot, depth));
        }

        public async Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            ImportStarted.TrySetResult();
            await meshImportNeverCompletes.Task;
            throw new InvalidOperationException("Unreachable.");
        }

        public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new Uri("resdb:///texture/0", UriKind.Absolute));
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        private Slot CloneSlot(Slot source, int depth)
        {
            Slot clone = new()
            {
                ID = source.ID,
                Parent = source.Parent is null ? null : new Reference
                {
                    TargetID = source.Parent.TargetID,
                },
                Name = source.Name is null ? null : new Field_string
                {
                    Value = source.Name.Value,
                },
                Position = source.Position,
                Rotation = source.Rotation,
                Scale = source.Scale,
            };

            if (source.Components is not null)
            {
                clone.Components = [.. source.Components];
            }

            if (depth <= 0)
            {
                return clone;
            }

            clone.Children = slotsById.Values
                .Where(slot => string.Equals(slot.Parent?.TargetID, source.ID, StringComparison.Ordinal))
                .Select(slot => CloneSlot(slot, depth - 1))
                .ToList();
            return clone;
        }
    }

    private sealed class LaggyExistingDatasetRootClient(
        FakeResoniteLinkSession session,
        string hiddenDatasetRootName,
        int hiddenPollCount) : IResoniteLinkClient
    {
        private int remainingHiddenPolls = hiddenPollCount;

        public void Dispose()
        {
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            string createdSlotId = string.IsNullOrWhiteSpace(request.Data.ID)
                ? session.AllocateSlotId()
                : request.Data.ID;
            lock (session.Gate)
            {
                request.Data.ID = createdSlotId;
                session.SlotsById[createdSlotId] = request.Data;
                session.AddedSlots.Add(request);
                session.SlotPaths[createdSlotId] = CreateSlotPath(session.SlotPaths, request.Data);
            }

            return Task.FromResult(createdSlotId);
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Unexpected batch mutation during dataset-root setup test.");
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                session.ComponentsById.TryGetValue(componentId, out Component? component);
                return Task.FromResult(component);
            }
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                if (string.Equals(slotId, "Root", StringComparison.Ordinal))
                {
                    Slot root = CreateSyntheticRootSlot(session, depth, CloneSlot);
                    if (depth > 0 && remainingHiddenPolls > 0 && root.Children is not null)
                    {
                        root.Children = root.Children
                            .Where(child => !string.Equals(child.Name?.Value, hiddenDatasetRootName, StringComparison.Ordinal))
                            .ToList();
                        remainingHiddenPolls--;
                    }

                    return Task.FromResult<Slot?>(root);
                }

                session.SlotsById.TryGetValue(slotId, out Slot? slot);
                return Task.FromResult(slot is null ? null : CloneSlot(slot, depth));
            }
        }

        public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Unexpected mesh import during dataset-root setup test.");
        }

        public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Unexpected texture import during dataset-root setup test.");
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                if (session.ComponentsById.TryGetValue(request.Data.ID, out Component? existing))
                {
                    foreach ((string memberName, Member member) in request.Data.Members)
                    {
                        existing.Members[memberName] = member;
                    }
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

            clone.Children = session.SlotsById.Values
                .Where(slot => string.Equals(slot.Parent?.TargetID, source.ID, StringComparison.Ordinal))
                .Select(slot => CloneSlot(slot, depth - 1))
                .ToList();
            return clone;
        }
    }

    private sealed class FakeResoniteLinkSession
    {
        private int nextComponentId;
        private int nextSlotId;

        public object Gate { get; } = new();

        public List<AddComponent> AddedComponents { get; } = [];

        public List<AddSlot> AddedSlots { get; } = [];

        public List<IReadOnlyList<DataModelOperation>> Batches { get; } = [];

        public Dictionary<string, string> BuildingSlotIds { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Component> ComponentsById { get; } = new(StringComparer.Ordinal);

        public List<ImportMeshRawData> ImportedMeshes { get; } = [];

        public List<string> ImportedTexturePaths { get; } = [];

        public List<ResoniteRawTextureImport> ImportedRawTextures { get; } = [];

        public List<ResoniteRawHdrTextureImport> ImportedRawHdrTextures { get; } = [];

        public Dictionary<string, string> SlotPaths { get; } = new(StringComparer.Ordinal);

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

    private static string CreateSlotPath(IReadOnlyDictionary<string, string> slotPaths, Slot slot)
    {
        string slotName = slot.Name?.Value ?? slot.ID;
        if (slot.Parent is null || string.Equals(slot.Parent.TargetID, "Root", StringComparison.Ordinal))
        {
            return slotName;
        }

        return string.Concat(slotPaths[slot.Parent.TargetID], "/", slotName);
    }

    private static Slot CreateSyntheticRootSlot(
        FakeResoniteLinkSession session,
        int depth,
        Func<Slot, int, Slot> cloneSlot)
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

        root.Children = session.SlotsById.Values
            .Where(static slot => string.Equals(slot.Parent?.TargetID, "Root", StringComparison.Ordinal))
            .Select(slot => cloneSlot(slot, depth - 1))
            .ToList();
        return root;
    }

    private static Slot CreateSyntheticRootSlot(
        SessionScopedMutationSession session,
        int depth,
        Func<Slot, int, Slot> cloneSlot)
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

        root.Children = session.SlotsById.Values
            .Where(static slot => string.Equals(slot.Parent?.TargetID, "Root", StringComparison.Ordinal))
            .Select(slot => cloneSlot(slot, depth - 1))
            .ToList();
        return root;
    }

    private static Slot FindSlotByName(
        IReadOnlyDictionary<string, Slot> slotsById,
        string slotName)
    {
        Slot[] matches = slotsById.Values
            .Where(slot => string.Equals(slot.Name?.Value, slotName, StringComparison.Ordinal))
            .ToArray();
        return SelectPreferredSlot(slotsById, matches);
    }

    private static Slot FindSlotByNameUnderAncestor(
        IReadOnlyDictionary<string, Slot> slotsById,
        string ancestorSlotId,
        string slotName)
    {
        Slot[] matches = slotsById.Values
            .Where(slot =>
                string.Equals(slot.Name?.Value, slotName, StringComparison.Ordinal)
                && IsDescendantOf(slotsById, slot, ancestorSlotId))
            .ToArray();
        return SelectPreferredSlot(slotsById, matches);
    }

    private static Slot SelectPreferredSlot(
        IReadOnlyDictionary<string, Slot> slotsById,
        Slot[] matches)
    {
        if (matches.Length == 1)
        {
            return matches[0];
        }

        Slot[] positionedMatches = matches.Where(static slot => slot.Position is not null).ToArray();
        if (positionedMatches.Length == 1)
        {
            return positionedMatches[0];
        }

        Slot[] componentRichMatches = matches
            .OrderByDescending(static slot => slot.Components?.Count ?? 0)
            .ToArray();
        if ((componentRichMatches[0].Components?.Count ?? 0) > (componentRichMatches[1].Components?.Count ?? 0))
        {
            return componentRichMatches[0];
        }

        Slot[] descendantRichMatches = matches
            .OrderByDescending(slot => CountDescendants(slotsById, slot.ID))
            .ToArray();
        if (CountDescendants(slotsById, descendantRichMatches[0].ID) > CountDescendants(slotsById, descendantRichMatches[1].ID))
        {
            return descendantRichMatches[0];
        }

        return Assert.Single(matches);
    }

    private static int CountDescendants(IReadOnlyDictionary<string, Slot> slotsById, string slotId)
    {
        return slotsById.Values.Count(slot => IsDescendantOf(slotsById, slot, slotId));
    }

    private static Slot FindDirectChildSlotByName(
        IReadOnlyDictionary<string, Slot> slotsById,
        string parentSlotId,
        string slotName)
    {
        return Assert.Single(
            slotsById.Values,
            slot =>
                string.Equals(slot.Name?.Value, slotName, StringComparison.Ordinal)
                && string.Equals(slot.Parent?.TargetID, parentSlotId, StringComparison.Ordinal));
    }

    private static bool IsDescendantOf(
        IReadOnlyDictionary<string, Slot> slotsById,
        Slot slot,
        string ancestorSlotId)
    {
        Reference? parent = slot.Parent;
        while (parent is not null && slotsById.TryGetValue(parent.TargetID, out Slot? parentSlot))
        {
            if (string.Equals(parentSlot.ID, ancestorSlotId, StringComparison.Ordinal))
            {
                return true;
            }

            parent = parentSlot.Parent;
        }

        return false;
    }

    private static string GetSlotPath(FakeResoniteLinkClient client, string slotId)
    {
        return client.SlotPaths[slotId];
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

    private static async Task<string[]> GetDirectChildNamesAsync(FakeResoniteLinkClient client, string parentId)
    {
        Slot parentSlot = Assert.IsType<Slot>(await client.GetSlotAsync(parentId, 1, CancellationToken.None));
        return (parentSlot.Children ?? [])
            .Select(static slot => slot.Name?.Value ?? slot.ID)
            .ToArray();
    }

    private static bool HasSlotPath(FakeResoniteLinkClient client, string expectedPath)
    {
        return client.SlotPaths.Values.Contains(expectedPath, StringComparer.Ordinal);
    }

    private static void AssertEqualSlotPosition(Slot expected, Slot actual)
    {
        Field_float3 expectedPosition = Assert.IsType<Field_float3>(expected.Position);
        Field_float3 actualPosition = Assert.IsType<Field_float3>(actual.Position);
        Assert.Equal(expectedPosition.Value.x, actualPosition.Value.x, precision: 4);
        Assert.Equal(expectedPosition.Value.y, actualPosition.Value.y, precision: 4);
        Assert.Equal(expectedPosition.Value.z, actualPosition.Value.z, precision: 4);
    }

    private static string FormatLodSlotName(int? lodLevel)
    {
        return lodLevel.HasValue
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"LOD{lodLevel.Value}")
            : "LOD0";
    }

    private static async Task<IReadOnlyList<string>> RunBuilderAsync(
        ResoniteLinkSceneBuilder builder,
        CapturedResoniteScene scene,
        string? workRoot = null)
    {
        using TemporaryDirectory? workDirectory = workRoot is null ? new TemporaryDirectory() : null;
        try
        {
            await builder.BeginAsync(scene.Metadata, workRoot ?? workDirectory!.Path);
            foreach (ResoniteConstructionCityObject cityObject in scene.CityObjects)
            {
                await builder.ProcessCityObjectAsync(cityObject);
            }

            return await builder.CompleteAsync();
        }
        finally
        {
            await builder.DisposeAsync();
        }
    }

    private static CapturedResoniteScene LoadScene(PlateauImportRequest request)
    {
        return SceneCache.GetOrAdd(request, static importRequest =>
        {
            IResoniteConstructionSource source = LocalCityGmlResonitePlanBuilder.CreateConstructionSource(importRequest);
            return new CapturedResoniteScene(source.Metadata, source.ReadCityObjects().ToArray());
        });
    }

    private static ResoniteConstructionCityObject CreateDispatchTestCityObject(
        string slotKey,
        string sourceObjectKey)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: slotKey,
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: CreateTriangleMesh(slotKey),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: sourceObjectKey);
    }

    private sealed record CapturedResoniteScene(
        ResoniteConstructionMetadata Metadata,
        IReadOnlyList<ResoniteConstructionCityObject> CityObjects);

    private static CapturedResoniteScene CreateAppendScene(string meshCode, string buildingName)
    {
        ResoniteConstructionMetadata metadata = new(
            SchemaVersion: "3.0",
            WorldName: $"PLATEAU tokyo23ku {meshCode}",
            Request: new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: meshCode,
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: TestData.GetFixturePath("LocalPlateauDataset"),
                ServerUri: null),
            SourceDataset: new PlateauSourceDataset(
                PackageNames: ["bldg", "dem"],
                SourceFiles: ["udx/dem/533945/plateau_tokyo23ku_dem_533945.gml", $"udx/bldg/{meshCode}/plateau_tokyo23ku_bldg_{meshCode}.gml"],
                TerrainTextureOverlays: []),
            Attribution: new ResoniteAttribution(
                DatasetLicense: new ResoniteLicenseComponentMetadata(
                    RequireCredit: true,
                    CreditText: "PLATEAU Open Data Terms",
                    LicenseName: "PLATEAU Open Data Terms",
                    LicenseUrl: "https://www.mlit.go.jp/plateau/site-policy/"),
                MaterialLicenses: []),
            LocalOrigin: meshCode switch
            {
                "53394525" => new ResoniteLocalOrigin(35.6875, 139.69375, 0.0),
                "53394526" => new ResoniteLocalOrigin(35.6875, 139.70625, 0.0),
                _ => throw new InvalidOperationException($"Unexpected mesh code '{meshCode}'."),
            });

        return new CapturedResoniteScene(
            metadata,
            [
                new ResoniteConstructionCityObject(
                    SlotKey: "dem_shared",
                    DisplayName: "Shared Terrain",
                    PackageName: "dem",
                    ActualMeshCode: "533945",
                    LodLevel: null,
                    Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
                    Mesh: CreateTriangleMesh("dem-material"),
                    Materials:
                    [
                        new ResoniteMaterialBinding(
                            MaterialKey: "dem-material",
                            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                            MaterialType: ResoniteMaterialType.Standard,
                            TexturePath: null,
                            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                            Projection: ResoniteMaterialProjection.Uv,
                            DepthOffset: null,
                            SubmeshIndices: [0]),
                    ],
                    SourceObjectKey: "udx_dem_533945_plateau_tokyo23ku_dem_533945_gml_dem_shared"),
                new ResoniteConstructionCityObject(
                    SlotKey: $"bldg_{meshCode}",
                    DisplayName: buildingName,
                    PackageName: "bldg",
                    ActualMeshCode: meshCode,
                    LodLevel: 2,
                    Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
                    Mesh: CreateTriangleMesh($"bldg-{meshCode}"),
                    Materials:
                    [
                        new ResoniteMaterialBinding(
                            MaterialKey: $"bldg-{meshCode}",
                            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                            MaterialType: ResoniteMaterialType.Standard,
                            TexturePath: null,
                            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                            Projection: ResoniteMaterialProjection.Uv,
                            DepthOffset: null,
                            SubmeshIndices: [0]),
                    ]),
            ]);
    }

    private static CapturedResoniteScene CreateCombinedAppendScene()
    {
        ResoniteConstructionMetadata metadata = new(
            SchemaVersion: "3.0",
            WorldName: "PLATEAU tokyo23ku 53394525",
            Request: new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: TestData.GetFixturePath("LocalPlateauDataset"),
                ServerUri: null),
            SourceDataset: new PlateauSourceDataset(
                PackageNames: ["bldg", "dem"],
                SourceFiles:
                [
                    "udx/dem/533945/plateau_tokyo23ku_dem_533945.gml",
                    "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
                    "udx/bldg/53394526/plateau_tokyo23ku_bldg_53394526.gml",
                ],
                TerrainTextureOverlays: []),
            Attribution: new ResoniteAttribution(
                DatasetLicense: new ResoniteLicenseComponentMetadata(
                    RequireCredit: true,
                    CreditText: "PLATEAU Open Data Terms",
                    LicenseName: "PLATEAU Open Data Terms",
                    LicenseUrl: "https://www.mlit.go.jp/plateau/site-policy/"),
                MaterialLicenses: []),
            LocalOrigin: new ResoniteLocalOrigin(35.6875, 139.69375, 0.0));

        return new CapturedResoniteScene(
            metadata,
            [
                new ResoniteConstructionCityObject(
                    SlotKey: "dem_shared",
                    DisplayName: "Shared Terrain",
                    PackageName: "dem",
                    ActualMeshCode: "533945",
                    LodLevel: null,
                    Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
                    Mesh: CreateTriangleMesh("dem-material"),
                    Materials:
                    [
                        new ResoniteMaterialBinding(
                            MaterialKey: "dem-material",
                            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                            MaterialType: ResoniteMaterialType.Standard,
                            TexturePath: null,
                            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                            Projection: ResoniteMaterialProjection.Uv,
                            DepthOffset: null,
                            SubmeshIndices: [0]),
                    ],
                    SourceObjectKey: "udx_dem_533945_plateau_tokyo23ku_dem_533945_gml_dem_shared"),
                new ResoniteConstructionCityObject(
                    SlotKey: "bldg_53394525",
                    DisplayName: "Building 25",
                    PackageName: "bldg",
                    ActualMeshCode: "53394525",
                    LodLevel: 2,
                    Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
                    Mesh: CreateTriangleMesh("bldg-53394525"),
                    Materials:
                    [
                        new ResoniteMaterialBinding(
                            MaterialKey: "bldg-53394525",
                            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                            MaterialType: ResoniteMaterialType.Standard,
                            TexturePath: null,
                            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                            Projection: ResoniteMaterialProjection.Uv,
                            DepthOffset: null,
                            SubmeshIndices: [0]),
                    ]),
                new ResoniteConstructionCityObject(
                    SlotKey: "bldg_53394526",
                    DisplayName: "Building 26",
                    PackageName: "bldg",
                    ActualMeshCode: "53394526",
                    LodLevel: 2,
                    Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
                    Mesh: CreateTriangleMesh("bldg-53394526"),
                    Materials:
                    [
                        new ResoniteMaterialBinding(
                            MaterialKey: "bldg-53394526",
                            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                            MaterialType: ResoniteMaterialType.Standard,
                            TexturePath: null,
                            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                            Projection: ResoniteMaterialProjection.Uv,
                            DepthOffset: null,
                            SubmeshIndices: [0]),
                    ]),
            ]);
    }

    private static CapturedResoniteScene CreateExactParentMeshScene()
    {
        ResoniteConstructionMetadata metadata = new(
            SchemaVersion: "3.0",
            WorldName: "PLATEAU tokyo23ku 533945",
            Request: new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "533945",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: TestData.GetFixturePath("LocalPlateauDataset"),
                ServerUri: null),
            SourceDataset: new PlateauSourceDataset(
                PackageNames: ["dem"],
                SourceFiles:
                [
                    "udx/dem/533945/plateau_tokyo23ku_dem_533945.gml",
                ],
                TerrainTextureOverlays: []),
            Attribution: new ResoniteAttribution(
                DatasetLicense: new ResoniteLicenseComponentMetadata(
                    RequireCredit: true,
                    CreditText: "PLATEAU Open Data Terms",
                    LicenseName: "PLATEAU Open Data Terms",
                    LicenseUrl: "https://www.mlit.go.jp/plateau/site-policy/"),
                MaterialLicenses: []),
            LocalOrigin: new ResoniteLocalOrigin(35.6875, 139.6875, 0.0));

        return new CapturedResoniteScene(
            metadata,
            [
                new ResoniteConstructionCityObject(
                    SlotKey: "dem_shared_parent",
                    DisplayName: "Shared Terrain",
                    PackageName: "dem",
                    ActualMeshCode: "533945",
                    LodLevel: null,
                    Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
                    Mesh: CreateTriangleMesh("dem-material"),
                    Materials:
                    [
                        new ResoniteMaterialBinding(
                            MaterialKey: "dem-material",
                            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                            MaterialType: ResoniteMaterialType.Standard,
                            TexturePath: null,
                            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                            Projection: ResoniteMaterialProjection.Uv,
                            DepthOffset: null,
                            SubmeshIndices: [0]),
                    ],
                    SourceObjectKey: "udx_dem_533945_plateau_tokyo23ku_dem_533945_gml_dem_shared_parent"),
            ]);
    }

    private static CapturedResoniteScene CreateRegexRequestScene()
    {
        return CreateRegexRequestScene("5339452[56]", new ResoniteLocalOrigin(35.6875, 139.69375, 0.0));
    }

    private static CapturedResoniteScene CreateRegexRequestScene(string meshCode)
    {
        return CreateRegexRequestScene(meshCode, new ResoniteLocalOrigin(35.6875, 139.69375, 0.0));
    }

    private static CapturedResoniteScene CreateRegexRequestScene(ResoniteLocalOrigin localOrigin)
    {
        return CreateRegexRequestScene("5339452[56]", localOrigin);
    }

    private static CapturedResoniteScene CreateRegexRequestScene(string meshCode, ResoniteLocalOrigin localOrigin)
    {
        ResoniteConstructionMetadata metadata = new(
            SchemaVersion: "3.0",
            WorldName: $"PLATEAU tokyo23ku {meshCode}",
            Request: new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: meshCode,
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: TestData.GetFixturePath("LocalPlateauDataset"),
                ServerUri: null),
            SourceDataset: new PlateauSourceDataset(
                PackageNames: ["bldg", "dem"],
                SourceFiles: ["udx/dem/533945/plateau_tokyo23ku_dem_533945.gml", "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"],
                TerrainTextureOverlays: [],
                RequestedMeshCodes: ["53394525"]),
            Attribution: new ResoniteAttribution(
                DatasetLicense: new ResoniteLicenseComponentMetadata(
                    RequireCredit: true,
                    CreditText: "PLATEAU Open Data Terms",
                    LicenseName: "PLATEAU Open Data Terms",
                    LicenseUrl: "https://www.mlit.go.jp/plateau/site-policy/"),
                MaterialLicenses: []),
            LocalOrigin: localOrigin);

        return new CapturedResoniteScene(
            metadata,
            [
                new ResoniteConstructionCityObject(
                    SlotKey: "dem_shared",
                    DisplayName: "Shared Terrain",
                    PackageName: "dem",
                    ActualMeshCode: "533945",
                    LodLevel: null,
                    Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
                    Mesh: CreateTriangleMesh("dem-material"),
                    Materials:
                    [
                        new ResoniteMaterialBinding(
                            MaterialKey: "dem-material",
                            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                            MaterialType: ResoniteMaterialType.Standard,
                            TexturePath: null,
                            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                            Projection: ResoniteMaterialProjection.Uv,
                            DepthOffset: null,
                            SubmeshIndices: [0]),
                    ],
                    SourceObjectKey: "shared-terrain"),
                new ResoniteConstructionCityObject(
                    SlotKey: "bldg_53394525",
                    DisplayName: "Building 25",
                    PackageName: "bldg",
                    ActualMeshCode: "53394525",
                    LodLevel: 2,
                    Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
                    Mesh: CreateTriangleMesh("bldg-53394525"),
                    Materials:
                    [
                        new ResoniteMaterialBinding(
                            MaterialKey: "bldg-53394525",
                            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                            MaterialType: ResoniteMaterialType.Standard,
                            TexturePath: null,
                            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                            Projection: ResoniteMaterialProjection.Uv,
                            DepthOffset: null,
                            SubmeshIndices: [0]),
                    ],
                    SourceObjectKey: "building-25"),
            ]);
    }

    private static ResoniteImportedMesh CreateTriangleMesh(string materialKey)
    {
        return new ResoniteImportedMesh(
            Vertices:
            [
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
            ],
            Submeshes:
            [
                new ResoniteMeshSubmesh(0, materialKey, [0, 1, 2]),
            ]);
    }

    private static ResoniteFloat3 ComputeOriginOffsetForTest(
        ResoniteLocalOrigin referenceCenter,
        ResoniteLocalOrigin currentCenter)
    {
        LocalCartesian cartesian = new(
            referenceCenter.Latitude,
            referenceCenter.Longitude,
            referenceCenter.Altitude,
            Geocentric.WGS84);
        (double x, double y, double z) eun = cartesian.Forward(
            currentCenter.Latitude,
            currentCenter.Longitude,
            currentCenter.Altitude);
        return new ResoniteFloat3(eun.x, eun.z, eun.y);
    }

}
