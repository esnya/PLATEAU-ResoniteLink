using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class NonDemCityObjectBakerTests
{
    [Fact]
    public async Task FlushAllAsyncBakesSingleSourceUnitIntoSingleMaterialAndSubmesh()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 1);

        await AssertBufferedAsync(baker, CreateLod2Building("building-one", CreateCheckerPayload("textures/one.png", new Rgba32(255, 0, 0, 255), new Rgba32(255, 255, 0, 255), 4, 4), 0, "unit-a"));
        await AssertBufferedAsync(baker, CreateLod2Building("building-two", CreateCheckerPayload("textures/two.png", new Rgba32(0, 255, 0, 255), new Rgba32(0, 255, 255, 255), 4, 4), 2, "unit-a"));

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        ResoniteConstructionCityObject cityObject = Assert.Single(baked);
        Assert.Single(cityObject.Materials);
        Assert.Single(cityObject.Mesh.Submeshes);
        Assert.Equal(6, cityObject.Mesh.Vertices.Count);
        Assert.Equal("unit-a.gml", cityObject.SourceFileRelativePath);
        RawRgba32ResoniteTexturePayload atlasPayload = Assert.IsType<RawRgba32ResoniteTexturePayload>(cityObject.Materials[0].TexturePayload);
        Assert.InRange(atlasPayload.Width, 1, 32);
        Assert.InRange(atlasPayload.Height, 1, 32);
        Assert.Equal(CommonMaterialCatalog.Create().Generic.Uv, cityObject.Materials[0].CommonMaterial);
    }

    [Fact]
    public async Task FlushAllAsyncTreatsPackageNameCaseVariantsAsTheSameBatch()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 1);
        await AssertBufferedAsync(baker, CreateLod2Building("building-one", CreateCheckerPayload("textures/one.png", new Rgba32(255, 0, 0, 255), new Rgba32(255, 255, 0, 255), 4, 4), 0, "unit-a"));
        await AssertBufferedAsync(baker, CreateLod2Building("building-two", CreateCheckerPayload("textures/two.png", new Rgba32(0, 255, 0, 255), new Rgba32(0, 255, 255, 255), 4, 4), 2, "unit-a") with { PackageName = "BLDG" });

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());

        Assert.Equal(6, cityObject.Mesh.Vertices.Count);
    }

    [Fact]
    public async Task FlushAllAsyncBakesActualUsedUvRegionInsteadOfWholeSourceTexture()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 0);

        await AssertBufferedAsync(baker, CreateUvScaledLod2Building(
            "building-one",
            CreateStripedPayload("textures/striped.png", [new Rgba32(255, 0, 0, 255), new Rgba32(0, 255, 0, 255), new Rgba32(0, 0, 255, 255), new Rgba32(255, 255, 0, 255)]),
            "unit-a",
            new ResoniteFloat2(0.25, 1.0),
            new ResoniteFloat2(0.5, 0.0)));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        RawRgba32ResoniteTexturePayload atlasPayload = Assert.IsType<RawRgba32ResoniteTexturePayload>(cityObject.Materials[0].TexturePayload);
        Assert.Equal(1, atlasPayload.Width);
        Assert.Equal(1, atlasPayload.Height);
        Assert.Null(cityObject.Materials[0].TextureScale);
        Assert.Null(cityObject.Materials[0].TextureOffset);
        Assert.Equal(CommonMaterialCatalog.Create().Generic.Uv, cityObject.Materials[0].CommonMaterial);
        Assert.Equal(new Rgba32(0, 0, 255, 255), ReadPixel(atlasPayload, 0, 0));
    }

    [Fact]
    public async Task FlushAllAsyncConvertsUniformDatasetTextureToSharedVertexColorMaterial()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 0);

        await AssertBufferedAsync(
            baker,
            CreateLod2Building(
                "building-uniform",
                CreatePayload("textures/uniform-red.png", new Rgba32(255, 0, 0, 255), 8, 8),
                0,
                "unit-a"));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        ResoniteMaterialBinding material = Assert.Single(cityObject.Materials);
        Assert.Equal(ResoniteMaterialType.VertexColor, material.MaterialType);
        Assert.Null(material.TexturePayload);
        Assert.Equal(ResoniteMaterialAssetScope.Common, material.AssetScope);
        Assert.Equal(new ResoniteColor(1.0, 1.0, 1.0, 1.0), material.BaseColor);
        Assert.All(cityObject.Mesh.Vertices, static vertex => Assert.Equal(new ResoniteColor(1.0, 0.0, 0.0, 1.0), vertex.Color));
    }

    [Fact]
    public async Task FlushAllAsyncOrdersPreservedPayloadMaterialsByFirstTraversalOccurrence()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 0);
        ResoniteTexturePayload payloadB = CreateCheckerPayload(
            "textures/b.png",
            new Rgba32(0, 255, 0, 255),
            new Rgba32(0, 0, 255, 255),
            4,
            4);
        ResoniteTexturePayload payloadA = CreateCheckerPayload(
            "textures/a.png",
            new Rgba32(255, 0, 0, 255),
            new Rgba32(255, 255, 0, 255),
            4,
            4);

        await AssertBufferedAsync(
            baker,
            CreateCommonPayloadPreservedLod2Building("building-0", payloadB, 0.0, "unit-a"));
        await AssertBufferedAsync(
            baker,
            CreateCommonPayloadPreservedLod2Building("building-1", payloadA, 2.0, "unit-a"));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());

        ResoniteTexturePayload?[] payloads = cityObject.Materials
            .Select(static material => material.TexturePayload)
            .ToArray();
        Assert.Collection(
            payloads,
            payload => Assert.Same(payloadB, payload),
            payload => Assert.Same(payloadA, payload));
    }

    [Fact]
    public async Task FlushAllAsyncBakesAlbedoOnlyCommonPayloadMaterialIntoGenericAtlasMaterial()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 0);
        ResoniteTexturePayload payload = CreateCheckerPayload(
            "textures/common-albedo.png",
            new Rgba32(255, 0, 0, 255),
            new Rgba32(0, 255, 0, 255),
            4,
            4);

        await AssertBufferedAsync(
            baker,
            CreateCommonPayloadPresentationScopedLod2Building("building-common-albedo", payload, "unit-a"));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        ResoniteMaterialBinding material = Assert.Single(cityObject.Materials);

        Assert.Equal(CommonMaterialCatalog.Create().Generic.Uv, material.CommonMaterial);
        Assert.NotSame(payload, material.TexturePayload);
        Assert.NotNull(material.TexturePayload);
        Assert.IsType<RawRgba32ResoniteTexturePayload>(material.TexturePayload);
        Assert.Null(material.TextureScale);
        Assert.Null(material.TextureOffset);
    }

    [Fact]
    public async Task FlushAllAsyncKeepsPreservedMaterialSubmeshIndicesAlignedWithGeometry()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 0);
        ResoniteTexturePayload payloadLeft = CreateCheckerPayload(
            "textures/left.png",
            new Rgba32(255, 0, 0, 255),
            new Rgba32(255, 255, 0, 255),
            4,
            4);
        ResoniteTexturePayload payloadRight = CreateCheckerPayload(
            "textures/right.png",
            new Rgba32(0, 255, 0, 255),
            new Rgba32(0, 0, 255, 255),
            4,
            4);

        await AssertBufferedAsync(
            baker,
            CreateCommonPayloadPreservedLod2Building("building-left", payloadLeft, 0.0, "unit-a"));
        await AssertBufferedAsync(
            baker,
            CreateCommonPayloadPreservedLod2Building("building-right", payloadRight, 10.0, "unit-a"));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());

        Dictionary<ResoniteTexturePayload, double> averageXByPayload = new(ReferenceEqualityComparer.Instance);
        foreach (ResoniteMaterialBinding material in cityObject.Materials)
        {
            int submeshIndex = Assert.Single(material.SubmeshIndices);
            ResoniteMeshSubmesh submesh = Assert.Single(
                cityObject.Mesh.Submeshes,
                candidate => candidate.Index == submeshIndex);
            double averageX = submesh.TriangleVertexIndices
                .Select(index => cityObject.Mesh.Vertices[index].Position.X)
                .Average();
            averageXByPayload.Add(material.TexturePayload ?? throw new InvalidOperationException("Preserved material must keep a texture payload."), averageX);
        }

        Assert.True(averageXByPayload[payloadLeft] < averageXByPayload[payloadRight]);
    }

    [Fact]
    public async Task FlushAllAsyncKeepsBakedUniformRegionFromNonUniformDatasetTextureAtBakedResolution()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 0);

        await AssertBufferedAsync(baker, CreateUvScaledLod2Building(
            "building-nonuniform",
            CreateStripedPayload("textures/nonuniform-red-region.png", [new Rgba32(255, 0, 0, 255), new Rgba32(255, 0, 0, 255), new Rgba32(0, 255, 0, 255), new Rgba32(0, 0, 255, 255)]),
            "unit-a",
            new ResoniteFloat2(0.5, 1.0),
            new ResoniteFloat2(0.0, 0.0)));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        RawRgba32ResoniteTexturePayload atlasPayload = Assert.IsType<RawRgba32ResoniteTexturePayload>(Assert.Single(cityObject.Materials).TexturePayload);

        Assert.Equal(2, atlasPayload.Width);
        Assert.Equal(1, atlasPayload.Height);
        Assert.Equal(new Rgba32(255, 0, 0, 255), ReadPixel(atlasPayload, 0, 0));
        Assert.Equal(new Rgba32(255, 0, 0, 255), ReadPixel(atlasPayload, 1, 0));
    }

    [Fact]
    public async Task FlushAllAsyncKeepsUniformVertexColorAndNonUniformAtlasMaterialsInSameBatch()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 0);

        await AssertBufferedAsync(
            baker,
            CreateLod2Building(
                "building-uniform",
                CreatePayload("textures/uniform-red.png", new Rgba32(255, 0, 0, 255), 8, 8),
                0,
                "unit-a"));
        await AssertBufferedAsync(
            baker,
            CreateLod2Building(
                "building-nonuniform",
                CreateCheckerPayload("textures/nonuniform.png", new Rgba32(0, 255, 0, 255), new Rgba32(0, 0, 255, 255), 4, 4),
                2,
                "unit-a"));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        Assert.Equal(2, cityObject.Materials.Count);
        Assert.Equal(2, cityObject.Mesh.Submeshes.Count);
        Assert.Contains(cityObject.Materials, static material => material.MaterialType == ResoniteMaterialType.VertexColor && material.TexturePayload is null);
        Assert.Contains(cityObject.Materials, static material => material.MaterialType == ResoniteMaterialType.Standard && material.TexturePayload is not null);
        Assert.Contains(cityObject.Mesh.Vertices, static vertex => vertex.Color == new ResoniteColor(1.0, 0.0, 0.0, 1.0));
    }

    [Fact]
    public async Task FlushAllAsyncRepeatsTextureContentWhenUsedUvRangeExceedsUnitSquare()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 0);

        await AssertBufferedAsync(baker, CreateUvScaledLod2Building(
            "building-one",
            CreateStripedPayload("textures/repeat.png", [new Rgba32(255, 0, 0, 255), new Rgba32(0, 255, 0, 255)]),
            "unit-a",
            new ResoniteFloat2(2.0, 1.0),
            null));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        RawRgba32ResoniteTexturePayload atlasPayload = Assert.IsType<RawRgba32ResoniteTexturePayload>(cityObject.Materials[0].TexturePayload);
        Assert.Equal(4, atlasPayload.Width);
        Assert.Equal(1, atlasPayload.Height);
        Assert.Equal(new Rgba32(255, 0, 0, 255), ReadPixel(atlasPayload, 0, 0));
        Assert.Equal(new Rgba32(0, 255, 0, 255), ReadPixel(atlasPayload, 1, 0));
        Assert.Equal(new Rgba32(255, 0, 0, 255), ReadPixel(atlasPayload, 2, 0));
        Assert.Equal(new Rgba32(0, 255, 0, 255), ReadPixel(atlasPayload, 3, 0));
    }

    [Fact]
    public async Task FlushAllAsyncNormalizesRepeatedSourceUvIntoAtlasSpace()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 0);

        await AssertBufferedAsync(baker, CreateUvScaledLod2Building(
            "building-offset-repeat",
            CreateStripedPayload("textures/offset-repeat.png", [new Rgba32(255, 0, 0, 255), new Rgba32(0, 255, 0, 255)]),
            "unit-a",
            new ResoniteFloat2(2.0, 1.0),
            new ResoniteFloat2(0.5, 0.0)));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        ResoniteMaterialBinding material = Assert.Single(cityObject.Materials);
        RawRgba32ResoniteTexturePayload atlasPayload = Assert.IsType<RawRgba32ResoniteTexturePayload>(material.TexturePayload);

        Assert.Null(material.TextureScale);
        Assert.Null(material.TextureOffset);
        Assert.Equal(4, atlasPayload.Width);
        Assert.Equal(1, atlasPayload.Height);
        Assert.Equal(new Rgba32(0, 255, 0, 255), ReadPixel(atlasPayload, 0, 0));
        Assert.Equal(new Rgba32(255, 0, 0, 255), ReadPixel(atlasPayload, 1, 0));
        Assert.Equal(new Rgba32(0, 255, 0, 255), ReadPixel(atlasPayload, 2, 0));
        Assert.Equal(new Rgba32(255, 0, 0, 255), ReadPixel(atlasPayload, 3, 0));
        Assert.All(
            cityObject.Mesh.Vertices,
            static vertex =>
            {
                Assert.InRange(vertex.UV0.X, 0.0, 1.0);
                Assert.InRange(vertex.UV0.Y, 0.0, 1.0);
            });
    }

    [Fact]
    public async Task FlushAllAsyncPreservesDetectedBackgroundColorInTransparentTilePixels()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 0);

        await AssertBufferedAsync(
            baker,
            CreateLod2Building(
                "building-transparent",
                CreatePayload(
                    "textures/transparent-edge.png",
                    [
                        new Rgba32(255, 0, 0, 255),
                        new Rgba32(0, 0, 0, 0),
                    ],
                    2,
                    1),
                0,
                "unit-a"));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        RawRgba32ResoniteTexturePayload atlasPayload = Assert.IsType<RawRgba32ResoniteTexturePayload>(cityObject.Materials[0].TexturePayload);

        Assert.Equal(2, atlasPayload.Width);
        Assert.Equal(1, atlasPayload.Height);
        Assert.Equal(new Rgba32(255, 0, 0, 255), ReadPixel(atlasPayload, 0, 0));
        Assert.Equal(new Rgba32(255, 0, 0, 0), ReadPixel(atlasPayload, 1, 0));
    }

    [Fact]
    public async Task FlushAllAsyncKeepsCommonMaterialsAsSeparateSubmeshesWhileAtlasingDedicatedMaterials()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 1);

        await AssertBufferedAsync(baker, CreateMixedScopeLod2Building("building-one", CreateCheckerPayload("textures/one.png", new Rgba32(255, 0, 0, 255), new Rgba32(255, 255, 0, 255), 4, 4), "unit-a"));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        Assert.Equal(2, cityObject.Materials.Count);
        Assert.Equal(2, cityObject.Mesh.Submeshes.Count);
        Assert.Contains(
            cityObject.Materials,
            static material => string.Equals(material.Family, BundledDefaultMaterialFamilies.FacadeHighriseGlass, StringComparison.Ordinal)
                && material.AssetScope == ResoniteMaterialAssetScope.PresentationSlotScoped);
        Assert.Contains(cityObject.Materials, static material => material.TexturePayload is not null);
    }

    [Fact]
    public async Task FlushAllAsyncDemotesBundledFacadeCommonTransformToMeshUv()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 1);

        await AssertBufferedAsync(baker, CreateFacadeCommonLod2Building("facade-common", "unit-a"));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());

        ResoniteMaterialBinding material = Assert.Single(cityObject.Materials);
        Assert.Equal(BundledDefaultMaterialFamilies.FacadeHighriseGlass, material.Family);
        Assert.Equal(ResoniteMaterialAssetScope.Common, material.AssetScope);
        Assert.Null(material.TextureScale);
        Assert.Null(material.TextureOffset);
        Assert.Equal(0.0, cityObject.Mesh.Vertices[0].UV0.X, 12);
        Assert.Equal(5.0 / 6.0, cityObject.Mesh.Vertices[0].UV0.Y, 12);
        Assert.Equal(10.0 / 6.0, cityObject.Mesh.Vertices[1].UV0.X, 12);
        Assert.Equal(5.0 / 6.0, cityObject.Mesh.Vertices[1].UV0.Y, 12);
        Assert.Equal(0.0, cityObject.Mesh.Vertices[2].UV0.X, 12);
        Assert.Equal(15.0 / 6.0, cityObject.Mesh.Vertices[2].UV0.Y, 12);
    }

    [Fact]
    public async Task FlushAllAsyncKeepsTintedPrescopedCommonMaterialVariantsDedicated()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 1);

        await AssertBufferedAsync(
            baker,
            CreateCommonVariantMixedLod2Building(
                "building-one",
                CreatePayload("textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4),
                "unit-a"));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        ResoniteMaterialBinding[] preservedFacadeMaterials = cityObject.Materials
            .Where(static material => string.Equals(material.Family, BundledDefaultMaterialFamilies.FacadeHighriseGlass, StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(3, cityObject.Materials.Count);
        Assert.Equal(3, cityObject.Mesh.Submeshes.Count);
        Assert.Equal(2, preservedFacadeMaterials.Length);
        Assert.All(preservedFacadeMaterials, static material => Assert.Equal(ResoniteMaterialAssetScope.PresentationSlotScoped, material.AssetScope));
        Assert.Contains(preservedFacadeMaterials, static material => material.BundledVariantIndex == 0 && material.BaseColor == new ResoniteColor(0.5, 0.5, 0.5, 1.0));
        Assert.Contains(preservedFacadeMaterials, static material => material.BundledVariantIndex == 1 && material.BaseColor == new ResoniteColor(0.5, 0.5, 0.5, 1.0));
    }

    [Fact]
    public async Task FlushAllAsyncKeepsTintedPreservedBundledFamilyMaterialsDedicated()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 1);

        await AssertBufferedAsync(
            baker,
            CreateBundledFamilyPreservedLod2Building(
                "building-one",
                CreatePayload("textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4),
                "unit-a"));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        ResoniteMaterialBinding[] preservedRoofMaterials = cityObject.Materials
            .Where(static material => string.Equals(material.Family, BundledDefaultMaterialFamilies.Roof, StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(3, cityObject.Materials.Count);
        Assert.Equal(3, cityObject.Mesh.Submeshes.Count);
        Assert.Equal(2, preservedRoofMaterials.Length);
        Assert.All(preservedRoofMaterials, static material => Assert.Equal(ResoniteMaterialAssetScope.PresentationSlotScoped, material.AssetScope));
        Assert.Contains(preservedRoofMaterials, static material => material.BundledVariantIndex == 0 && material.BaseColor == new ResoniteColor(0.85, 0.85, 0.85, 1.0));
        Assert.Contains(preservedRoofMaterials, static material => material.BundledVariantIndex == 1 && material.BaseColor == new ResoniteColor(0.75, 0.75, 0.75, 1.0));
        Assert.All(preservedRoofMaterials, static material => Assert.Null(material.TexturePayload));
    }

    [Fact]
    public async Task FlushAllAsyncKeepsWhitePreservedBundledFamilyMaterialsDedicatedWhenOffsetOrDepthExists()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 1);
        ResoniteConstructionCityObject source = CreateBundledFamilyPreservedLod2Building(
            "building-transform",
            CreatePayload("textures/transform.png", new Rgba32(255, 0, 0, 255), 4, 4),
            "unit-a") with
        {
            Materials =
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: CreatePayload("textures/transform.png", new Rgba32(255, 0, 0, 255), 4, 4),
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
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
                    DepthOffset: new ResoniteMaterialDepthOffset(1.0, 1.0),
                    SubmeshIndices: [1],
                                        AssetBinding: ResoniteMaterialAssetBinding.Presentation,
                    Family: BundledDefaultMaterialFamilies.Roof,
                    TextureOffset: new ResoniteFloat2(0.125, 0.25),
                    BundledVariantIndex: 0),
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: new ResoniteMaterialDepthOffset(2.0, 2.0),
                    SubmeshIndices: [2],
                                        AssetBinding: ResoniteMaterialAssetBinding.Presentation,
                    Family: BundledDefaultMaterialFamilies.Roof,
                    TextureOffset: new ResoniteFloat2(0.25, 0.5),
                    BundledVariantIndex: 1),
            ],
        };

        await AssertBufferedAsync(baker, source);

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        ResoniteMaterialBinding[] preservedRoofMaterials = cityObject.Materials
            .Where(static material => string.Equals(material.Family, BundledDefaultMaterialFamilies.Roof, StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(3, cityObject.Materials.Count);
        Assert.Equal(3, cityObject.Mesh.Submeshes.Count);
        Assert.Equal(2, preservedRoofMaterials.Length);
        Assert.All(preservedRoofMaterials, static material => Assert.Equal(ResoniteMaterialAssetScope.PresentationSlotScoped, material.AssetScope));
        Assert.Contains(preservedRoofMaterials, static material => material.BundledVariantIndex == 0 && material.TextureOffset is null);
        Assert.Contains(preservedRoofMaterials, static material => material.BundledVariantIndex == 1 && material.TextureOffset is null);
        Assert.NotEqual(new ResoniteFloat2(0.0, 0.0), cityObject.Mesh.Vertices[3].UV0);
        Assert.NotEqual(new ResoniteFloat2(0.0, 0.0), cityObject.Mesh.Vertices[6].UV0);
    }

    [Fact]
    public async Task FlushAllAsyncAllowsTintedPreservedBundledFamilyMaterialsWhenCommonPreservationIsDisabled()
    {
        NonDemCityObjectBaker baker = CreateBaker(
            maxAtlasSize: 32,
            tilePaddingPixels: 1,
            bakePolicies:
            [
                new NonDemCityObjectBakePolicy(
                    Name: "dedicated-bundled-check",
                    CanBufferCityObject: static _ => true,
                    RequireAtlasCandidateMaterial: true,
                    PreserveVertexColorMaterials: true,
                    PreserveTexturelessMaterials: false,
                    PreserveCommonMaterials: false),
            ]);

        await AssertBufferedAsync(
            baker,
            CreateBundledFamilyPreservedLod2Building(
                "building-policy-check",
                CreatePayload("textures/policy-check.png", new Rgba32(255, 0, 0, 255), 4, 4),
                "unit-a"));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        ResoniteMaterialBinding[] preservedRoofMaterials = cityObject.Materials
            .Where(static material => string.Equals(material.Family, BundledDefaultMaterialFamilies.Roof, StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(3, cityObject.Materials.Count);
        Assert.Equal(2, preservedRoofMaterials.Length);
        Assert.All(preservedRoofMaterials, static material => Assert.Equal(ResoniteMaterialAssetScope.PresentationSlotScoped, material.AssetScope));
    }

    [Fact]
    public async Task FlushAllAsyncDemotesPrescopedWhiteBundledFamilyMaterialsWhenOffsetOrDepthExists()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 1);
        ResoniteConstructionCityObject source = CreateBundledFamilyPreservedLod2Building(
            "building-prescoped-transform",
            CreatePayload("textures/prescoped-transform.png", new Rgba32(255, 0, 0, 255), 4, 4),
            "unit-a") with
        {
            Materials =
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: CreatePayload("textures/prescoped-transform.png", new Rgba32(255, 0, 0, 255), 4, 4),
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
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
                    DepthOffset: new ResoniteMaterialDepthOffset(1.0, 1.0),
                    SubmeshIndices: [1],
                    Family: BundledDefaultMaterialFamilies.Roof,
                    TextureOffset: new ResoniteFloat2(0.125, 0.25),
                    BundledVariantIndex: 0,
                    AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedBundled(BundledDefaultMaterialFamilies.Roof, 0)),
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: new ResoniteMaterialDepthOffset(2.0, 2.0),
                    SubmeshIndices: [2],
                    Family: BundledDefaultMaterialFamilies.Roof,
                    TextureOffset: new ResoniteFloat2(0.25, 0.5),
                    BundledVariantIndex: 1,
                    AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedBundled(BundledDefaultMaterialFamilies.Roof, 1)),
            ],
        };

        await AssertBufferedAsync(baker, source);

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        ResoniteMaterialBinding[] preservedRoofMaterials = cityObject.Materials
            .Where(static material => string.Equals(material.Family, BundledDefaultMaterialFamilies.Roof, StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(3, cityObject.Materials.Count);
        Assert.Equal(2, preservedRoofMaterials.Length);
        Assert.All(preservedRoofMaterials, static material => Assert.Equal(ResoniteMaterialAssetScope.PresentationSlotScoped, material.AssetScope));
        Assert.Contains(preservedRoofMaterials, static material => material.BundledVariantIndex == 0 && material.TextureOffset is null);
        Assert.Contains(preservedRoofMaterials, static material => material.BundledVariantIndex == 1 && material.TextureOffset is null);
    }

    [Fact]
    public async Task FlushAllAsyncFallsBackToOriginalCityObjectWhenSingleCandidateCannotFitAtlasBudget()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 12, tilePaddingPixels: 1);
        ResoniteConstructionCityObject oversizedCandidate = CreateMultiTextureLod2Building(
            "building-one",
            CreateCheckerPayload("textures/one.png", new Rgba32(255, 0, 0, 255), new Rgba32(255, 255, 0, 255), 12, 12),
            CreateCheckerPayload("textures/two.png", new Rgba32(0, 255, 0, 255), new Rgba32(0, 0, 255, 255), 12, 12),
            "unit-a");

        await AssertBufferedAsync(baker, oversizedCandidate);

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        Assert.Equal(oversizedCandidate.SlotKey, cityObject.SlotKey);
        Assert.Equal(oversizedCandidate.DisplayName, cityObject.DisplayName);
        Assert.Equal(oversizedCandidate.Materials.Count, cityObject.Materials.Count);
        Assert.All(cityObject.Materials, static material => Assert.NotNull(material.TexturePayload));
    }

    [Fact]
    public async Task FlushAllAsyncFallsBackToNormalizedCityObjectWhenSingleCandidateCannotFitAtlasBudget()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 10, tilePaddingPixels: 0);
        ResoniteConstructionCityObject oversizedCandidate = CreateUvScaledLod2Building(
            "building-dynamic-fallback",
            CreateCheckerPayload("textures/dynamic-fallback.png", new Rgba32(255, 0, 0, 255), new Rgba32(0, 255, 0, 255), 9, 3),
            "unit-a",
            new ResoniteFloat2(2.0, 0.5),
            new ResoniteFloat2(0.25, 0.75));

        await AssertBufferedAsync(baker, oversizedCandidate);

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        ResoniteMaterialBinding material = Assert.Single(cityObject.Materials);
        Assert.Equal(oversizedCandidate.SlotKey, cityObject.SlotKey);
        Assert.Null(material.TextureScale);
        Assert.Null(material.TextureOffset);
        Assert.Equal(3, cityObject.Mesh.Vertices.Count);
        Assert.Equal(new ResoniteFloat2(0.25, 0.75), cityObject.Mesh.Vertices[0].UV0);
        Assert.Equal(new ResoniteFloat2(2.25, 0.75), cityObject.Mesh.Vertices[1].UV0);
        Assert.Equal(new ResoniteFloat2(0.25, 1.25), cityObject.Mesh.Vertices[2].UV0);
    }

    [Fact]
    public async Task FlushAllAsyncBakesAlbedoOnlyFamilyMaterialsWithinCityObjectIntoSingleAtlasMaterial()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 0);

        await AssertBufferedAsync(
            baker,
            CreateAlbedoFamilyLod2Building(
                "building-family-albedo",
                CreatePayload("textures/family-red.png", new Rgba32(255, 0, 0, 255), 1, 1),
                CreatePayload("textures/family-green.png", new Rgba32(0, 255, 0, 255), 1, 1),
                "unit-a"));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        Assert.Single(cityObject.Materials);
        Assert.Single(cityObject.Mesh.Submeshes);
        ResoniteMaterialBinding material = Assert.Single(cityObject.Materials);
        Assert.Equal(ResoniteMaterialType.VertexColor, material.MaterialType);
        Assert.Null(material.TexturePayload);

        HashSet<ResoniteColor> vertexColors = cityObject.Mesh.Vertices
            .Select(static vertex => vertex.Color)
            .OfType<ResoniteColor>()
            .ToHashSet();
        Assert.Contains(new ResoniteColor(1.0, 0.0, 0.0, 1.0), vertexColors);
        Assert.Contains(new ResoniteColor(0.0, 1.0, 0.0, 1.0), vertexColors);
    }

    [Fact]
    public async Task FlushAllAsyncMergesStructurallyIdenticalWhiteBundledFamilyMaterialsForLod1Batches()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 1);
        ResoniteConstructionCityObject source = CreateBundledFamilyPreservedLod2Building(
            "building-lod1-roof",
            CreatePayload("textures/lod1-roof.png", new Rgba32(255, 0, 0, 255), 4, 4),
            "unit-a") with
        {
            LodLevel = 1,
            Materials =
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: CreatePayload("textures/lod1-roof.png", new Rgba32(255, 0, 0, 255), 4, 4),
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
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
                    SubmeshIndices: [1],
                                        AssetBinding: ResoniteMaterialAssetBinding.Presentation,
                    Family: BundledDefaultMaterialFamilies.Roof,
                    BundledVariantIndex: 0),
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [2],
                                        AssetBinding: ResoniteMaterialAssetBinding.Presentation,
                    Family: BundledDefaultMaterialFamilies.Roof,
                    BundledVariantIndex: 0),
            ],
        };

        await AssertBufferedAsync(baker, source);

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        ResoniteMaterialBinding[] preservedRoofMaterials = cityObject.Materials
            .Where(static material => string.Equals(material.Family, BundledDefaultMaterialFamilies.Roof, StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(2, cityObject.Materials.Count);
        Assert.Equal(2, cityObject.Mesh.Submeshes.Count);
        ResoniteMaterialBinding preservedRoofMaterial = Assert.Single(preservedRoofMaterials);
        Assert.All(preservedRoofMaterials, static material => Assert.Equal(ResoniteMaterialAssetScope.PresentationSlotScoped, material.AssetScope));
        int preservedRoofSubmeshIndex = Assert.Single(preservedRoofMaterial.SubmeshIndices);
        Assert.Contains(
            cityObject.Mesh.Submeshes,
            submesh => submesh.Index == preservedRoofSubmeshIndex);
    }

    [Fact]
    public async Task FlushAllAsyncKeepsDistinctSourceUnitsInSeparateAtlasBatches()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 1);

        await AssertBufferedAsync(baker, CreateLod2Building("building-one", CreatePayload("textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4), 0, "unit-a"));
        await AssertBufferedAsync(baker, CreateLod2Building("building-two", CreatePayload("textures/two.png", new Rgba32(0, 255, 0, 255), 4, 4), 2, "unit-b"));

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        Assert.Equal(2, baked.Count);
        Assert.Contains(baked, static cityObject => cityObject.SourceFileRelativePath == "unit-a.gml");
        Assert.Contains(baked, static cityObject => cityObject.SourceFileRelativePath == "unit-b.gml");
    }

    [Fact]
    public async Task FlushAllAsyncMergesSameSourceFileAcrossDifferentSourceUnits()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 1);
        await AssertBufferedAsync(baker, CreateLod2Building("building-one", CreatePayload("textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4), 0, "unit-a") with
        {
            SourceFileRelativePath = "common.gml",
        });
        await AssertBufferedAsync(baker, CreateLod2Building("building-two", CreatePayload("textures/two.png", new Rgba32(0, 255, 0, 255), 4, 4), 2, "unit-b") with
        {
            SourceFileRelativePath = "common.gml",
        });

        ResoniteConstructionCityObject baked = Assert.Single(await baker.FlushAllAsync());

        Assert.Equal("common.gml", baked.SourceFileRelativePath);
    }

    [Fact]
    public async Task FlushAllAsyncKeepsSameSourceFileInSingleAtlasBatchAcrossDifferentSourceUnits()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 1);

        await AssertBufferedAsync(
            baker,
            CreateLod2Building("building-one", CreatePayload("textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4), 0, "unit-a") with
            {
                SourceFileRelativePath = "shared.gml",
            });
        await AssertBufferedAsync(
            baker,
            CreateLod2Building("building-two", CreatePayload("textures/two.png", new Rgba32(0, 255, 0, 255), 4, 4), 2, "unit-b") with
            {
                SourceFileRelativePath = "shared.gml",
            });

        ResoniteConstructionCityObject baked = Assert.Single(await baker.FlushAllAsync());
        Assert.Equal("shared.gml", baked.SourceFileRelativePath);
    }

    [Fact]
    public async Task FlushAllAsyncPacksMixedSizeTexturesIntoSingleAtlasBatch()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 16, tilePaddingPixels: 0);

        await AssertBufferedAsync(baker, CreateLod2Building("building-a", CreateCheckerPayload("textures/a.png", new Rgba32(255, 0, 0, 255), new Rgba32(255, 255, 0, 255), 7, 7), 0, "unit-a"));
        await AssertBufferedAsync(baker, CreateLod2Building("building-b", CreateCheckerPayload("textures/b.png", new Rgba32(0, 255, 0, 255), new Rgba32(0, 255, 255, 255), 1, 7), 2, "unit-a"));
        await AssertBufferedAsync(baker, CreateLod2Building("building-c", CreateCheckerPayload("textures/c.png", new Rgba32(0, 0, 255, 255), new Rgba32(255, 0, 255, 255), 3, 3), 4, "unit-a"));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        RawRgba32ResoniteTexturePayload atlasPayload = Assert.IsType<RawRgba32ResoniteTexturePayload>(cityObject.Materials[0].TexturePayload);
        Assert.Equal(16, atlasPayload.Width);
        Assert.Equal(8, atlasPayload.Height);
    }

    [Fact]
    public async Task FlushAllAsyncFallsBackWhenSingleCandidateNeedsNonPowerOfTwoEdgeBeyondBudget()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 10, tilePaddingPixels: 0);

        ResoniteConstructionCityObject oversizedCandidate = CreateLod2Building(
            "building-a",
            CreatePayload("textures/a.png", new Rgba32(255, 0, 0, 255), 9, 3),
            0,
            "unit-a");

        await AssertBufferedAsync(baker, oversizedCandidate);

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        Assert.Equal(oversizedCandidate.SlotKey, cityObject.SlotKey);
    }

    [Fact]
    public async Task TryBufferAsyncBuffersLod1NonDemCityObjectsAndNormalizesDynamicUvTransform()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 1);
        ResoniteConstructionCityObject cityObject = CreateUvScaledLod2Building(
            "lod1-dynamic",
            CreatePayload("textures/lod1-dynamic.png", new Rgba32(255, 0, 0, 255), 4, 4),
            "unit-a",
            new ResoniteFloat2(2.0, 0.5),
            new ResoniteFloat2(0.25, 0.75)) with
        {
            PackageName = "tran",
            LodLevel = 1,
        };

        BufferedCityObjectBufferResult result = await baker.TryBufferAsync(cityObject);

        Assert.True(result.Buffered);
        Assert.Empty(result.ReadyCityObjects);
        ResoniteConstructionCityObject baked = Assert.Single(await baker.FlushAllAsync());
        ResoniteMaterialBinding material = Assert.Single(baked.Materials);
        Assert.Null(material.TextureScale);
        Assert.Null(material.TextureOffset);
        Assert.Equal(3, baked.Mesh.Vertices.Count);
        Assert.Equal("tran", baked.PackageName);
    }

    [Fact]
    public async Task TryBufferAsyncPreservesTerrainOverlayAlbedoOnlyProviderWithGenericCommonIdentity()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 1);
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        ResoniteConstructionCityObject firstCityObject = CreateLod2Building(
            "lod1-terrain-overlay",
            CreatePayload("textures/unused.png", new Rgba32(255, 0, 0, 255), 4, 4),
            0,
            "unit-a") with
        {
            LodLevel = 1,
            Materials =
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    TerrainOverlayMaterial: new TerrainOverlayMaterialBinding(overlay.MeshCode, overlay),
                    AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedGenericUv()),
            ],
        };
        ResoniteConstructionCityObject secondCityObject = firstCityObject with
        {
            SlotKey = "lod1-terrain-overlay-b",
            DisplayName = "lod1-terrain-overlay-b",
            Transform = new ResoniteTransform(new ResoniteFloat3(2.0, 0.0, 0.0)),
        };

        await AssertBufferedAsync(baker, firstCityObject);
        await AssertBufferedAsync(baker, secondCityObject);

        ResoniteConstructionCityObject baked = Assert.Single(await baker.FlushAllAsync());
        ResoniteMaterialBinding material = Assert.Single(baked.Materials);
        Assert.Equal(ResoniteMaterialAssetScope.Common, material.AssetScope);
        Assert.Equal(CommonMaterialCatalog.Create().Generic.Uv, material.CommonMaterial);
        Assert.Null(material.TexturePayload);
        Assert.Same(overlay, material.TerrainOverlay);
        Assert.Equal("53394525", material.TerrainMeshCode);
    }

    [Fact]
    public async Task TryBufferAsyncSkipsDemCityObjectsWithoutNormalizingDynamicUvTransform()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 1);
        ResoniteConstructionCityObject demCityObject = CreateUvScaledLod2Building(
            "dem-dynamic",
            CreatePayload("textures/dem-dynamic.png", new Rgba32(255, 0, 0, 255), 4, 4),
            "unit-a",
            new ResoniteFloat2(2.0, 0.5),
            new ResoniteFloat2(0.25, 0.75)) with
        {
            PackageName = "dem",
            LodLevel = null,
        };

        BufferedCityObjectBufferResult result = await baker.TryBufferAsync(demCityObject);

        Assert.False(result.Buffered);
        Assert.Empty(result.ReadyCityObjects);
        Assert.Equal(new ResoniteFloat2(2.0, 0.5), Assert.Single(demCityObject.Materials).TextureScale);
        Assert.Equal(new ResoniteFloat2(0.25, 0.75), Assert.Single(demCityObject.Materials).TextureOffset);
        Assert.Equal(3, demCityObject.Mesh.Vertices.Count);
        Assert.Empty(await baker.FlushAllAsync());
    }

    [Fact]
    public async Task TryBufferAsyncRejectsDuplicateMaterialBindingsBeforeDynamicUvNormalization()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 1);
        ResoniteConstructionCityObject cityObject = CreateUvScaledLod2Building(
            "duplicate-dynamic",
            CreatePayload("textures/duplicate-dynamic.png", new Rgba32(255, 0, 0, 255), 4, 4),
            "unit-a",
            new ResoniteFloat2(2.0, 0.5),
            new ResoniteFloat2(0.25, 0.75));
        ResoniteMaterialBinding material = Assert.Single(cityObject.Materials);
        cityObject = cityObject with
        {
            Materials =
            [
                material,
                material with
                {
                    BaseColor = new ResoniteColor(0.5, 1.0, 1.0, 1.0),
                },
            ],
        };

        BufferedCityObjectBufferResult result = await baker.TryBufferAsync(cityObject);

        Assert.False(result.Buffered);
        Assert.Empty(result.ReadyCityObjects);
        Assert.Empty(await baker.FlushAllAsync());
    }

    [Fact]
    public async Task TryBufferAsyncBuffersLodlessNonDemCityObjects()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 32, tilePaddingPixels: 1);
        ResoniteConstructionCityObject cityObject = CreateLod2Building(
            "lodless-frn",
            CreateCheckerPayload("textures/lodless-frn.png", new Rgba32(255, 0, 0, 255), new Rgba32(0, 255, 0, 255), 4, 4),
            0,
            "unit-a") with
        {
            PackageName = "frn",
            LodLevel = null,
        };

        BufferedCityObjectBufferResult result = await baker.TryBufferAsync(cityObject);

        Assert.True(result.Buffered);
        Assert.Empty(result.ReadyCityObjects);
        ResoniteConstructionCityObject baked = Assert.Single(await baker.FlushAllAsync());
        Assert.Null(baked.LodLevel);
        Assert.Equal("frn", baked.PackageName);
    }

    [Fact]
    public async Task FlushAllAsyncCapsAtlasTileSizeForSmallMemoryProfile()
    {
        NonDemCityObjectBaker baker = CreateBaker(
            maxAtlasSize: 2048,
            tilePaddingPixels: 0,
            resourceBudget: ResoniteImportBudgetProfiles.Small);

        await AssertBufferedAsync(
            baker,
            CreateLod2Building(
                "building-large",
                CreateVerticalSplitPayload("textures/large.png", new Rgba32(255, 0, 0, 255), new Rgba32(0, 0, 255, 255), 1024, 1024),
                0,
                "unit-a"));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        RawRgba32ResoniteTexturePayload atlasPayload = Assert.IsType<RawRgba32ResoniteTexturePayload>(cityObject.Materials[0].TexturePayload);
        Assert.Equal(512, atlasPayload.Width);
        Assert.Equal(512, atlasPayload.Height);
    }

    [Fact]
    public async Task FlushAllAsyncCountsOutputsAcceptedBeforeCallbackFailure()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 10, tilePaddingPixels: 0);
        await AssertBufferedAsync(
            baker,
            CreateLod2Building(
                "building-a",
                CreateCheckerPayload("textures/a.png", new Rgba32(255, 0, 0, 255), new Rgba32(255, 255, 0, 255), 9, 3),
                0,
                "unit-a"));
        await AssertBufferedAsync(
            baker,
            CreateLod2Building(
                "building-b",
                CreateCheckerPayload("textures/b.png", new Rgba32(0, 255, 0, 255), new Rgba32(0, 255, 255, 255), 9, 3),
                2,
                "unit-a"));

        int callbackCount = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => baker.FlushAllAsync(
            (_, _) =>
            {
                callbackCount++;
                if (callbackCount == 2)
                {
                    throw new InvalidOperationException("stop after first accepted output");
                }

                return Task.CompletedTask;
            }));

        Assert.Equal(2, callbackCount);
        Assert.Equal(1, baker.BakedOutputCityObjectCount);
    }

    [Fact]
    public async Task FlushAllAsyncAdvancesBatchIdentityAfterCallbackFailure()
    {
        NonDemCityObjectBaker baker = CreateBaker(maxAtlasSize: 16, tilePaddingPixels: 0);
        await AssertBufferedAsync(
            baker,
            CreateLod2Building(
                "building-a",
                CreateCheckerPayload("textures/a.png", new Rgba32(255, 0, 0, 255), new Rgba32(255, 255, 0, 255), 9, 9),
                0,
                "unit-a"));
        await AssertBufferedAsync(
            baker,
            CreateLod2Building(
                "building-b",
                CreateCheckerPayload("textures/b.png", new Rgba32(0, 255, 0, 255), new Rgba32(0, 255, 255, 255), 9, 9),
                2,
                "unit-a"));

        int callbackCount = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => baker.FlushAllAsync(
            (_, _) =>
            {
                callbackCount++;
                if (callbackCount == 2)
                {
                    throw new InvalidOperationException("stop after second reserved output");
                }

                return Task.CompletedTask;
            }));

        await AssertBufferedAsync(
            baker,
            CreateLod2Building(
                "building-c",
                CreateCheckerPayload("textures/c.png", new Rgba32(0, 0, 255, 255), new Rgba32(255, 0, 255, 255), 4, 4),
                4,
                "unit-a"));
        await AssertBufferedAsync(
            baker,
            CreateLod2Building(
                "building-d",
                CreateCheckerPayload("textures/d.png", new Rgba32(255, 255, 255, 255), new Rgba32(0, 0, 0, 255), 4, 4),
                6,
                "unit-a"));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());

        Assert.Equal("atlasbake-unit-a-bldg-lod2-3", cityObject.SlotKey);
        Assert.IsType<RawRgba32ResoniteTexturePayload>(Assert.Single(cityObject.Materials).TexturePayload);
    }

    private static NonDemCityObjectBaker CreateBaker(
        int maxAtlasSize = NonDemAtlasBakeBudget.DefaultMaxAtlasSize,
        int tilePaddingPixels = NonDemAtlasBakeBudget.DefaultTilePaddingPixels,
        IReadOnlyList<NonDemCityObjectBakePolicy>? bakePolicies = null,
        ResoniteImportBudgetProfile? resourceBudget = null)
    {
        return new NonDemCityObjectBaker(
            new NonDemCityObjectBakePolicyResolver(bakePolicies ?? NonDemCityObjectBakePolicies.DefaultPolicies),
            CreateSourceFileBakeEmitter(new NonDemAtlasBakeBudget(maxAtlasSize, tilePaddingPixels, resourceBudget)));
    }

    private static NonDemSourceFileBakeEmitter CreateSourceFileBakeEmitter(NonDemAtlasBakeBudget atlasBudget)
    {
        NonDemAtlasLayoutFactory layoutFactory = new(
            atlasBudget.EffectiveMaxAtlasSize,
            atlasBudget.TilePaddingPixels);
        return new NonDemSourceFileBakeEmitter(
            new NonDemCityObjectBakeCandidateFactory(
                new NonDemBakeEntryFactory(new ResoniteTextureImageLoader(), atlasBudget.EffectiveMaxAtlasTextureEdge)),
            new NonDemCityObjectBakeAssembler(
                layoutFactory,
                new NonDemAtlasImageRenderer(atlasBudget.TilePaddingPixels)),
            new NonDemAtlasBatchFitPolicy(layoutFactory));
    }

    private static async Task AssertBufferedAsync(NonDemCityObjectBaker baker, ResoniteConstructionCityObject cityObject)
    {
        BufferedCityObjectBufferResult result = await baker.TryBufferAsync(cityObject);
        Assert.True(result.Buffered);
        Assert.Empty(result.ReadyCityObjects);
    }

    private static ResoniteTexturePayload CreatePayload(string identity, Rgba32 color, int width, int height)
    {
        using Image<Rgba32> image = new(width, height, color);
        return ResoniteTextureImportFactory.CreatePayloadFromImage(image, description: identity);
    }

    private static ResoniteTexturePayload CreatePayload(string identity, IReadOnlyList<Rgba32> pixels, int width, int height)
    {
        Assert.Equal(width * height, pixels.Count);
        using Image<Rgba32> image = new(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                image[x, y] = pixels[(y * width) + x];
            }
        }

        return ResoniteTextureImportFactory.CreatePayloadFromImage(image, description: identity);
    }

    private static ResoniteTexturePayload CreateStripedPayload(string identity, IReadOnlyList<Rgba32> colors)
    {
        using Image<Rgba32> image = new(colors.Count, 1);
        for (int x = 0; x < colors.Count; x++)
        {
            image[x, 0] = colors[x];
        }

        return ResoniteTextureImportFactory.CreatePayloadFromImage(image, description: identity);
    }

    private static ResoniteTexturePayload CreateCheckerPayload(string identity, Rgba32 primary, Rgba32 secondary, int width, int height)
    {
        using Image<Rgba32> image = new(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                image[x, y] = ((x + y) & 1) == 0 ? primary : secondary;
            }
        }

        return ResoniteTextureImportFactory.CreatePayloadFromImage(image, description: identity);
    }

    private static ResoniteTexturePayload CreateVerticalSplitPayload(string identity, Rgba32 left, Rgba32 right, int width, int height)
    {
        using Image<Rgba32> image = new(width, height);
        int splitX = width / 2;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                image[x, y] = x < splitX ? left : right;
            }
        }

        return ResoniteTextureImportFactory.CreatePayloadFromImage(image, description: identity);
    }

    private static Rgba32 ReadPixel(RawRgba32ResoniteTexturePayload payload, int x, int y)
    {
        int width = payload.Width;
        int offset = ((y * width) + x) * 4;
        ReadOnlySpan<byte> bytes = payload.BinaryPayload.AsSpan();
        return new Rgba32(
            bytes[offset],
            bytes[offset + 1],
            bytes[offset + 2],
            bytes[offset + 3]);
    }

    private static ResoniteConstructionCityObject CreateLod2Building(
        string slotKey,
        ResoniteTexturePayload payload,
        double x,
        string sourceUnitKey)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: slotKey,
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
            Transform: new ResoniteTransform(new ResoniteFloat3(x, 0.0, 0.0)),
            Mesh: new ResoniteImportedMesh(
                [
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                ],
                [
                    new ResoniteMeshSubmesh(0, [0, 1, 2]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: payload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    ResoniteMaterialAssetBinding.Presentation),
            ],
            SourceFileRelativePath: $"{sourceUnitKey}.gml");
    }

    private static TerrainTextureOverlay CreateThirdMeshOverlay(string meshCode)
    {
        Assert.True(PlateauMeshCode.TryGetBounds(meshCode, out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds));
        return new TerrainTextureOverlay(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse(meshCode),
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 17,
            GeographicBounds: new GeographicRectangle(
                bounds.SouthLatitude,
                bounds.NorthLatitude,
                bounds.WestLongitude,
                bounds.EastLongitude),
            MaxTextureSize: 4096);
    }


    private static ResoniteConstructionCityObject CreateMultiTextureLod2Building(
        string slotKey,
        ResoniteTexturePayload firstPayload,
        ResoniteTexturePayload secondPayload,
        string sourceUnitKey)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: slotKey,
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: new ResoniteImportedMesh(
                [
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(2.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                ],
                [
                    new ResoniteMeshSubmesh(0, [0, 1, 2]),
                    new ResoniteMeshSubmesh(1, [3, 4, 5]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: firstPayload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    ResoniteMaterialAssetBinding.Presentation),
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: secondPayload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    ResoniteMaterialAssetBinding.Presentation),
            ],
            SourceFileRelativePath: $"{sourceUnitKey}.gml");
    }

    private static ResoniteConstructionCityObject CreateCommonPayloadPreservedLod2Building(
        string slotKey,
        ResoniteTexturePayload payload,
        double x,
        string sourceUnitKey)
    {
        DefaultCommonMaterialMember commonMaterial = CommonMaterialCatalog.Create().Generic.Uv;
        return CreateLod2Building(slotKey, payload, x, sourceUnitKey) with
        {
            Materials =
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: payload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    AssetBinding: ResoniteMaterialAssetBinding.SharedCommon(commonMaterial)),
            ],
        };
    }

    private static ResoniteConstructionCityObject CreateCommonPayloadPresentationScopedLod2Building(
        string slotKey,
        ResoniteTexturePayload payload,
        string sourceUnitKey)
    {
        return CreateLod2Building(slotKey, payload, 0.0, sourceUnitKey) with
        {
            Materials =
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: payload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    AssetBinding: ResoniteMaterialAssetBinding.PresentationCommon(CommonMaterialCatalog.Create().Generic.Uv)),
            ],
        };
    }

    private static ResoniteConstructionCityObject CreateMixedScopeLod2Building(
        string slotKey,
        ResoniteTexturePayload payload,
        string sourceUnitKey)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: slotKey,
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
                    new ResoniteMeshSubmesh(0, [0, 1, 2]),
                    new ResoniteMeshSubmesh(1, [1, 3, 2]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: payload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    ResoniteMaterialAssetBinding.Presentation),
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(0.4, 0.4, 0.4, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    Family: BundledDefaultMaterialFamilies.FacadeHighriseGlass,
            AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedBundled(BundledDefaultMaterialFamilies.FacadeHighriseGlass, 0),
                    BundledVariantIndex: 0),
            ],
            SourceFileRelativePath: $"{sourceUnitKey}.gml");
    }

    private static ResoniteConstructionCityObject CreateUvScaledLod2Building(
        string slotKey,
        ResoniteTexturePayload payload,
        string sourceUnitKey,
        ResoniteFloat2 textureScale,
        ResoniteFloat2? textureOffset)
    {
        return CreateLod2Building(slotKey, payload, 0.0, sourceUnitKey) with
        {
            Materials =
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: payload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                                        AssetBinding: ResoniteMaterialAssetBinding.Presentation,
                    TextureScale: textureScale,
                    TextureOffset: textureOffset),
            ],
        };
    }

    private static ResoniteConstructionCityObject CreateFacadeCommonLod2Building(
        string slotKey,
        string sourceUnitKey)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: slotKey,
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: new ResoniteImportedMesh(
                [
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                ],
                [
                    new ResoniteMeshSubmesh(0, [0, 1, 2]),
                ]),
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
                    TextureScale: new ResoniteFloat2(
                        BundledDefaultMaterialProfiles.FacadeDefaultTilesPerMeterValue.X,
                        BundledDefaultMaterialProfiles.FacadeDefaultTilesPerMeterValue.Y),
                    TextureOffset: new ResoniteFloat2(0.0, 0.5 / 6.0),
                    Family: BundledDefaultMaterialFamilies.FacadeHighriseGlass,
                    AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedBundled(BundledDefaultMaterialFamilies.FacadeHighriseGlass, 0),
                    BundledVariantIndex: 0),
            ],
            SourceFileRelativePath: $"{sourceUnitKey}.gml");
    }

    private static ResoniteConstructionCityObject CreateAlbedoFamilyLod2Building(
        string slotKey,
        ResoniteTexturePayload redFamilyTexture,
        ResoniteTexturePayload greenFamilyTexture,
        string sourceUnitKey)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: slotKey,
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: new ResoniteImportedMesh(
                [
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(2.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                ],
                [
                    new ResoniteMeshSubmesh(0, [0, 1, 2]),
                    new ResoniteMeshSubmesh(1, [3, 4, 5]),
                ]),
            Materials: [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: redFamilyTexture,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                                        AssetBinding: ResoniteMaterialAssetBinding.Presentation,
                    Family: BundledDefaultMaterialFamilies.Facade),
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: greenFamilyTexture,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                                        AssetBinding: ResoniteMaterialAssetBinding.Presentation,
                    Family: BundledDefaultMaterialFamilies.Facade),
            ],
            SourceFileRelativePath: $"{sourceUnitKey}.gml");
    }

    private static ResoniteConstructionCityObject CreateCommonVariantMixedLod2Building(
        string slotKey,
        ResoniteTexturePayload payload,
        string sourceUnitKey)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: slotKey,
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: new ResoniteImportedMesh(
                [
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(2.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(2.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(3.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(2.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                ],
                [
                    new ResoniteMeshSubmesh(0, [0, 1, 2]),
                    new ResoniteMeshSubmesh(1, [3, 4, 5]),
                    new ResoniteMeshSubmesh(2, [6, 7, 8]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: payload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    ResoniteMaterialAssetBinding.Presentation),
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(0.5, 0.5, 0.5, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    Family: BundledDefaultMaterialFamilies.FacadeHighriseGlass,
                    AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedBundled(BundledDefaultMaterialFamilies.FacadeHighriseGlass, 0),
                    BundledVariantIndex: 0),
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(0.5, 0.5, 0.5, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [2],
                    Family: BundledDefaultMaterialFamilies.FacadeHighriseGlass,
                    AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedBundled(BundledDefaultMaterialFamilies.FacadeHighriseGlass, 1),
                    BundledVariantIndex: 1),
            ],
            SourceFileRelativePath: $"{sourceUnitKey}.gml");
    }

    private static ResoniteConstructionCityObject CreateBundledFamilyPreservedLod2Building(
        string slotKey,
        ResoniteTexturePayload payload,
        string sourceUnitKey)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: slotKey,
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: new ResoniteImportedMesh(
                [
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(2.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(2.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(3.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(2.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                ],
                [
                    new ResoniteMeshSubmesh(0, [0, 1, 2]),
                    new ResoniteMeshSubmesh(1, [3, 4, 5]),
                    new ResoniteMeshSubmesh(2, [6, 7, 8]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: payload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    ResoniteMaterialAssetBinding.Presentation),
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(0.85, 0.85, 0.85, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                                        AssetBinding: ResoniteMaterialAssetBinding.Presentation,
                    Family: BundledDefaultMaterialFamilies.Roof,
                    BundledVariantIndex: 0),
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(0.75, 0.75, 0.75, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [2],
                                        AssetBinding: ResoniteMaterialAssetBinding.Presentation,
                    Family: BundledDefaultMaterialFamilies.Roof,
                    BundledVariantIndex: 1),
            ],
            SourceFileRelativePath: $"{sourceUnitKey}.gml");
    }
}
