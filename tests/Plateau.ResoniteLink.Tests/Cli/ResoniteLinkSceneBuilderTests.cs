using System.Diagnostics.CodeAnalysis;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Cli;

[SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "The test helper owns builder disposal for all streaming execution paths.")]
public sealed class ResoniteLinkSceneBuilderTests
{
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
            () => fakeClient);

        IReadOnlyList<string> destinations = await RunBuilderAsync(builder, scene);

        Assert.Single(destinations);
        Assert.Equal(6, fakeClient.ImportedTexturePaths.Count);
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
        Assert.Contains("PLATEAU tokyo23ku/Assets/Shared", fakeClient.SlotPaths.Values);
        Assert.Contains("PLATEAU tokyo23ku/Assets/Shared/Materials", fakeClient.SlotPaths.Values);
        Assert.Contains($"PLATEAU tokyo23ku/Assets/bldg/{buildingLodSlotName}/Building One", fakeClient.SlotPaths.Values);
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
        Assert.Equal(6, staticTextureRequests.Length);

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
        Assert.Contains(
            fakeClient.ImportedTexturePaths,
            static path => path.EndsWith("_Emission.jpg", StringComparison.Ordinal));

        AddComponent datasetTextureRequest = Assert.Single(
            staticTextureRequests,
            request =>
            {
                Field_Uri candidateUrl = Assert.IsType<Field_Uri>(request.Data.Members["URL"]);
                return string.Equals(candidateUrl.Value.ToString(), "resdb:///texture/0", StringComparison.Ordinal);
            });
        Slot textureAssetSlot = fakeClient.SlotsById[datasetTextureRequest.ContainerSlotId];
        Assert.StartsWith(
            "PLATEAU tokyo23ku/Assets/Shared/Materials/",
            fakeClient.SlotPaths[textureAssetSlot.ID],
            StringComparison.Ordinal);
        Component datasetTexture = datasetTextureRequest.Data;
        Field_Uri datasetTextureUrl = Assert.IsType<Field_Uri>(datasetTexture.Members["URL"]);
        Assert.Equal("resdb:///texture/0", datasetTextureUrl.Value.ToString());

        AddComponent bundledTextureRequest = Assert.Single(
            staticTextureRequests,
            request =>
            {
                Field_Uri candidateUrl = Assert.IsType<Field_Uri>(request.Data.Members["URL"]);
                return string.Equals(candidateUrl.Value.ToString(), "resdb:///texture/1", StringComparison.Ordinal);
            });
        Slot bundledTextureAssetSlot = fakeClient.SlotsById[bundledTextureRequest.ContainerSlotId];
        Assert.StartsWith(
            "PLATEAU tokyo23ku/Assets/Shared/Materials/",
            fakeClient.SlotPaths[bundledTextureAssetSlot.ID],
            StringComparison.Ordinal);
        Component bundledTexture = bundledTextureRequest.Data;
        Field_Uri bundledTextureUrl = Assert.IsType<Field_Uri>(bundledTexture.Members["URL"]);
        Assert.Equal("resdb:///texture/1", bundledTextureUrl.Value.ToString());

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
                $"PLATEAU tokyo23ku/Assets/bldg/{buildingLodSlotName}/Building One/Building One",
                StringComparison.Ordinal));

        AddComponent[] materialRequests = fakeClient.AddedComponents
            .Where(request =>
                string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal)
                && request.ContainerSlotId != fakeClient.BuildingSlotIds["Building One"])
            .ToArray();
        Assert.Equal(2, materialRequests.Length);
        Assert.All(materialRequests, request =>
        {
            Assert.StartsWith(
                "PLATEAU tokyo23ku/Assets/Shared/Materials/",
                fakeClient.SlotPaths[request.ContainerSlotId],
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
        Assert.IsType<Reference>(uvFacadeMaterial.Members["MetallicMap"]);
        Assert.IsType<Reference>(uvFacadeMaterial.Members["OcclusionMap"]);
        Field_float uvHeightScale = Assert.IsType<Field_float>(uvFacadeMaterial.Members["HeightScale"]);
        Assert.Equal(0.002f, uvHeightScale.Value);
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
    public async Task BuildAsyncKeepsStableEntityIdsAcrossRuns()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));

        using FakeResoniteLinkClient firstClient = new();
        using FakeResoniteLinkClient secondClient = new();

        await RunBuilderAsync(new ResoniteLinkSceneBuilder(new Uri("ws://localhost:12345/"), () => firstClient), scene);
        await RunBuilderAsync(new ResoniteLinkSceneBuilder(new Uri("ws://localhost:12345/"), () => secondClient), scene);

        HashSet<string> firstEntityIds = firstClient.AddedSlots
            .Select(static request => request.Data.ID)
            .Concat(firstClient.AddedComponents.Select(static request => request.Data.ID))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            firstEntityIds.OrderBy(static id => id, StringComparer.Ordinal),
            secondClient.AddedSlots
                .Select(static request => request.Data.ID)
                .Concat(secondClient.AddedComponents.Select(static request => request.Data.ID))
                .OrderBy(static id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task BuildAsyncAppendsDifferentMeshCodeAndSkipsSharedObjectsAlreadyPlaced()
    {
        FakeResoniteLinkSession session = new();
        using FakeResoniteLinkClient firstClient = new(session);
        using FakeResoniteLinkClient secondClient = new(session);

        CapturedResoniteScene firstScene = CreateAppendScene("53394525", "Building 25");
        CapturedResoniteScene secondScene = CreateAppendScene("53394526", "Building 26");

        await RunBuilderAsync(new ResoniteLinkSceneBuilder(new Uri("ws://localhost:12345/"), () => firstClient), firstScene);
        int importedMeshCountAfterFirstRun = firstClient.ImportedMeshes.Count;
        int addedParentMeshSlotsAfterFirstRun = firstClient.AddedSlots.Count(request =>
            GetSlotPath(firstClient, request.Data.ID).StartsWith("PLATEAU tokyo23ku/533945/", StringComparison.Ordinal));

        await RunBuilderAsync(new ResoniteLinkSceneBuilder(new Uri("ws://localhost:12345/"), () => secondClient), secondScene);

        Assert.Contains("PLATEAU tokyo23ku/53394525", secondClient.SlotPaths.Values);
        Assert.Contains("PLATEAU tokyo23ku/53394526", secondClient.SlotPaths.Values);

        Slot appendedMeshCodeSlot = secondClient.SlotsById[ResoniteLinkEntityIdFactory.CreateStableEntityId("tokyo23ku", "53394526", "meshcode")];
        Field_float3 appendedPosition = Assert.IsType<Field_float3>(appendedMeshCodeSlot.Position);
        Assert.NotEqual(0.0f, appendedPosition.Value.x);

        Assert.Equal(importedMeshCountAfterFirstRun + 1, secondClient.ImportedMeshes.Count);
        Assert.Equal(
            addedParentMeshSlotsAfterFirstRun,
            secondClient.AddedSlots.Count(request =>
                GetSlotPath(secondClient, request.Data.ID).StartsWith("PLATEAU tokyo23ku/533945/", StringComparison.Ordinal)));
        Assert.Contains("PLATEAU tokyo23ku/533945/dem/LOD0/Shared Terrain", secondClient.SlotPaths.Values);
        Assert.Contains("PLATEAU tokyo23ku/53394526/bldg/LOD2/Building 26", secondClient.SlotPaths.Values);
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
            () => fakeClient,
            terrainTextureAssetGenerator);

        await RunBuilderAsync(builder, scene);

        TerrainTextureOverlay requestedOverlay = Assert.Single(terrainTextureAssetGenerator.RequestedOverlays);
        Assert.Equal(LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath, requestedOverlay.TexturePath);

        string builtInTexturePath = Assert.Single(
            fakeClient.ImportedTexturePaths,
            static path => string.Equals(Path.GetFileName(path), "dem-overlay.png", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(builtInTexturePath));
    }

    [Fact]
    public async Task BuildAsyncReusesSharedAssetsWithinSession()
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
        using FakeResoniteLinkClient secondClient = new(session);

        await RunBuilderAsync(new ResoniteLinkSceneBuilder(new Uri("ws://localhost:12345/"), () => firstClient), scene);
        int importedTextureCountAfterFirstRun = firstClient.ImportedTexturePaths.Count;
        int importedMeshCountAfterFirstRun = firstClient.ImportedMeshes.Count;

        await RunBuilderAsync(new ResoniteLinkSceneBuilder(new Uri("ws://localhost:12345/"), () => secondClient), scene);

        Assert.Equal(importedTextureCountAfterFirstRun, secondClient.ImportedTexturePaths.Count);
        Assert.Equal(importedMeshCountAfterFirstRun, secondClient.ImportedMeshes.Count);
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
            () => blockingClient);

        await builder.BeginAsync(scene.Metadata, "runtime/resonite");
        await builder.ProcessCityObjectAsync(scene.CityObjects[0]);

        Task<IReadOnlyList<string>> completionTask = builder.CompleteAsync();
        Assert.False(completionTask.IsCompleted);

        blockingClient.ReleaseMeshImports();

        IReadOnlyList<string> destinations = await completionTask;
        Assert.Single(destinations);
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

        public List<AddComponent> AddedComponents => session.AddedComponents;

        public List<AddSlot> AddedSlots => session.AddedSlots;

        public Dictionary<string, string> BuildingSlotIds => session.BuildingSlotIds;

        public List<ImportMeshRawData> ImportedMeshes => session.ImportedMeshes;

        public List<string> ImportedTexturePaths => session.ImportedTexturePaths;

        public Dictionary<string, Slot> SlotsById => session.SlotsById;

        public Dictionary<string, string> SlotPaths => session.SlotPaths;

        public void Dispose()
        {
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.ComponentsById[request.Data.ID] = request.Data;
            session.AddedComponents.Add(request);
            return Task.CompletedTask;
        }

        public Task AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.SlotsById[request.Data.ID] = request.Data;
            session.AddedSlots.Add(request);

            string? slotName = request.Data.Name?.Value;
            string slotPath = CreateSlotPath(session.SlotPaths, request.Data);
            session.SlotPaths[request.Data.ID] = slotPath;

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
                session.BuildingSlotIds[slotName] = request.Data.ID;
            }

            return Task.CompletedTask;
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.ComponentsById.TryGetValue(componentId, out Component? component);
            return Task.FromResult(component);
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.SlotsById.TryGetValue(slotId, out Slot? slot);
            return Task.FromResult(slot is null ? null : CloneSlot(slot, depth));
        }

        public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.ImportedMeshes.Add(request);
            return Task.FromResult(new Uri($"resdb:///mesh/{session.ImportedMeshes.Count - 1}", UriKind.Absolute));
        }

        public Task<Uri> ImportTextureAsync(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.ImportedTexturePaths.Add(filePath);
            return Task.FromResult(new Uri($"resdb:///texture/{session.ImportedTexturePaths.Count - 1}", UriKind.Absolute));
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Component existing = session.ComponentsById[request.Data.ID];
            foreach ((string memberName, Member member) in request.Data.Members)
            {
                existing.Members[memberName] = member;
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

    private sealed class StubTerrainTextureAssetGenerator : ITerrainTextureAssetGenerator
    {
        private static readonly byte[] TextureBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAGklEQVR42mP8z8DQwMDA8J+BkYGBgQEADzYCAjUX0xMAAAAASUVORK5CYII=");

        public List<TerrainTextureOverlay> RequestedOverlays { get; } = [];

        public async Task<string> EnsureTextureAsync(
            TerrainTextureOverlay terrainTextureOverlay,
            string workRoot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedOverlays.Add(terrainTextureOverlay);

            string textureDirectory = Path.Combine(workRoot, "terrain-textures");
            Directory.CreateDirectory(textureDirectory);
            string texturePath = Path.Combine(textureDirectory, "dem-overlay.png");
            if (!File.Exists(texturePath))
            {
                await File.WriteAllBytesAsync(texturePath, TextureBytes, cancellationToken);
            }

            return texturePath;
        }
    }

    private sealed class BlockingResoniteLinkClient : IResoniteLinkClient
    {
        private readonly TaskCompletionSource meshImportRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose()
        {
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
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

        public async Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            await meshImportRelease.Task.WaitAsync(cancellationToken);
            return new Uri("resdb:///mesh/0", UriKind.Absolute);
        }

        public Task<Uri> ImportTextureAsync(string filePath, CancellationToken cancellationToken)
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

    private sealed class FakeResoniteLinkSession
    {
        public List<AddComponent> AddedComponents { get; } = [];

        public List<AddSlot> AddedSlots { get; } = [];

        public Dictionary<string, string> BuildingSlotIds { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Component> ComponentsById { get; } = new(StringComparer.Ordinal);

        public List<ImportMeshRawData> ImportedMeshes { get; } = [];

        public List<string> ImportedTexturePaths { get; } = [];

        public Dictionary<string, string> SlotPaths { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Slot> SlotsById { get; } = new(StringComparer.Ordinal);
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

    private static string GetSlotPath(FakeResoniteLinkClient client, string slotId)
    {
        return client.SlotPaths[slotId];
    }

    private static bool HasSlotPath(FakeResoniteLinkClient client, string expectedPath)
    {
        return client.SlotPaths.Values.Contains(expectedPath, StringComparer.Ordinal);
    }

    private static string FormatLodSlotName(int? lodLevel)
    {
        return lodLevel.HasValue
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"LOD{lodLevel.Value}")
            : "LOD0";
    }

    private static async Task<IReadOnlyList<string>> RunBuilderAsync(
        ResoniteLinkSceneBuilder builder,
        CapturedResoniteScene scene)
    {
        try
        {
            await builder.BeginAsync(scene.Metadata, "runtime/resonite");
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
        IResoniteConstructionSource source = LocalCityGmlResonitePlanBuilder.CreateConstructionSource(request);
        return new CapturedResoniteScene(source.Metadata, source.ReadCityObjects().ToArray());
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

}
