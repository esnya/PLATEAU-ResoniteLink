using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class Lod2AtlasCityObjectBakerTests
{
    [Fact]
    public async Task FlushAllAsyncBakesSingleSourceUnitIntoSingleMaterialAndSubmesh()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetImage(datasetRoot.Path, "textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4);
        WriteDatasetImage(datasetRoot.Path, "textures/two.png", new Rgba32(0, 255, 0, 255), 4, 4);
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        ResoniteTextureImportRegistry textureImportRegistry = new();
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            textureImportRegistry,
            maxAtlasSize: 32,
            tilePaddingPixels: 1);

        await AssertBufferedAsync(baker, CreateLod2Building("building-one", "textures/one.png", 0, "unit-a"));
        await AssertBufferedAsync(baker, CreateLod2Building("building-two", "textures/two.png", 2, "unit-a"));

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        ResoniteConstructionCityObject cityObject = Assert.Single(baked);
        Assert.Single(cityObject.Materials);
        Assert.Single(cityObject.Mesh.Submeshes);
        Assert.Equal(6, cityObject.Mesh.Vertices.Count);
        Assert.Equal("unit-a", cityObject.SourceUnitKey);
        Assert.True(textureImportRegistry.TryGet(
            cityObject.Materials[0].TexturePath!,
            cityObject.Materials[0].TextureSourceKind,
            out ResoniteTextureImport? textureImport));
        ResoniteRawTextureImport rawImport = Assert.IsType<ResoniteRawTextureImport>(textureImport);
        Assert.InRange(rawImport.Width, 1, 32);
        Assert.InRange(rawImport.Height, 1, 32);
    }

    [Fact]
    public async Task FlushAllAsyncBakesSingleCityObjectWithMultipleAlbedoTexturesIntoSingleAtlas()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetImage(datasetRoot.Path, "textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4);
        WriteDatasetImage(datasetRoot.Path, "textures/two.png", new Rgba32(0, 255, 0, 255), 4, 4);
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        ResoniteTextureImportRegistry textureImportRegistry = new();
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            textureImportRegistry,
            maxAtlasSize: 32,
            tilePaddingPixels: 1);

        await AssertBufferedAsync(baker, CreateMultiTextureLod2Building("building-one", "textures/one.png", "textures/two.png", "unit-a"));

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        ResoniteConstructionCityObject cityObject = Assert.Single(baked);
        Assert.Equal("building-one", cityObject.SlotKey);
        Assert.Single(cityObject.Materials);
        Assert.Single(cityObject.Mesh.Submeshes);
        Assert.Equal(6, cityObject.Mesh.Vertices.Count);
        Assert.Contains(cityObject.Mesh.Vertices, static vertex => vertex.UV0.X > 0.5);
        Assert.Contains(cityObject.Mesh.Vertices, static vertex => vertex.UV0.X < 0.5);
        Assert.True(textureImportRegistry.TryGet(
            cityObject.Materials[0].TexturePath!,
            cityObject.Materials[0].TextureSourceKind,
            out ResoniteTextureImport? textureImport));
        Assert.IsType<ResoniteRawTextureImport>(textureImport);
    }

    [Fact]
    public async Task FlushAllAsyncBakesActualUsedUvRegionInsteadOfWholeSourceTexture()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteStripedDatasetImage(
            datasetRoot.Path,
            "textures/striped.png",
            [new Rgba32(255, 0, 0, 255), new Rgba32(0, 255, 0, 255), new Rgba32(0, 0, 255, 255), new Rgba32(255, 255, 0, 255)]);
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        ResoniteTextureImportRegistry textureImportRegistry = new();
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            textureImportRegistry,
            maxAtlasSize: 32,
            tilePaddingPixels: 0);

        await AssertBufferedAsync(baker, CreateUvScaledLod2Building(
            "building-one",
            "textures/striped.png",
            "unit-a",
            new ResoniteFloat2(0.25, 1.0),
            new ResoniteFloat2(0.5, 0.0)));

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        ResoniteConstructionCityObject cityObject = Assert.Single(baked);
        Assert.Single(cityObject.Materials);
        Assert.True(textureImportRegistry.TryGet(
            cityObject.Materials[0].TexturePath!,
            cityObject.Materials[0].TextureSourceKind,
            out ResoniteTextureImport? textureImport));
        ResoniteRawTextureImport rawImport = Assert.IsType<ResoniteRawTextureImport>(textureImport);
        Assert.Equal(1, rawImport.Width);
        Assert.Equal(1, rawImport.Height);
        Assert.Equal(new Rgba32(0, 0, 255, 255), ReadPixel(rawImport, 0, 0));
    }

    [Fact]
    public async Task FlushAllAsyncRepeatsTextureContentWhenUsedUvRangeExceedsUnitSquare()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteStripedDatasetImage(
            datasetRoot.Path,
            "textures/repeat.png",
            [new Rgba32(255, 0, 0, 255), new Rgba32(0, 255, 0, 255)]);
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        ResoniteTextureImportRegistry textureImportRegistry = new();
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            textureImportRegistry,
            maxAtlasSize: 32,
            tilePaddingPixels: 0);

        await AssertBufferedAsync(baker, CreateUvScaledLod2Building(
            "building-one",
            "textures/repeat.png",
            "unit-a",
            new ResoniteFloat2(2.0, 1.0),
            null));

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        ResoniteConstructionCityObject cityObject = Assert.Single(baked);
        Assert.True(textureImportRegistry.TryGet(
            cityObject.Materials[0].TexturePath!,
            cityObject.Materials[0].TextureSourceKind,
            out ResoniteTextureImport? textureImport));
        ResoniteRawTextureImport rawImport = Assert.IsType<ResoniteRawTextureImport>(textureImport);
        Assert.Equal(4, rawImport.Width);
        Assert.Equal(1, rawImport.Height);
        Assert.Equal(new Rgba32(255, 0, 0, 255), ReadPixel(rawImport, 0, 0));
        Assert.Equal(new Rgba32(0, 255, 0, 255), ReadPixel(rawImport, 1, 0));
        Assert.Equal(new Rgba32(255, 0, 0, 255), ReadPixel(rawImport, 2, 0));
        Assert.Equal(new Rgba32(0, 255, 0, 255), ReadPixel(rawImport, 3, 0));
    }

    [Fact]
    public async Task FlushAllAsyncPreservesBilinearSampledAppearanceAcrossAtlasBakedTriangle()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteGradientDatasetImage(datasetRoot.Path, "textures/gradient.png", 8, 8);
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        ResoniteTextureImportRegistry textureImportRegistry = new();
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            textureImportRegistry,
            maxAtlasSize: 64,
            tilePaddingPixels: 1);

        ResoniteConstructionCityObject sourceCityObject = CreateUvScaledLod2Building(
            "building-one",
            "textures/gradient.png",
            "unit-a",
            new ResoniteFloat2(0.625, 0.5),
            new ResoniteFloat2(0.125, 0.25));

        await AssertBufferedAsync(baker, sourceCityObject);

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        ResoniteConstructionCityObject bakedCityObject = Assert.Single(baked);
        Assert.True(textureImportRegistry.TryGet(
            bakedCityObject.Materials[0].TexturePath!,
            bakedCityObject.Materials[0].TextureSourceKind,
            out ResoniteTextureImport? textureImport));
        ResoniteRawTextureImport atlasImport = Assert.IsType<ResoniteRawTextureImport>(textureImport);
        using Image<Rgba32> sourceImage = await Image.LoadAsync<Rgba32>(Path.Combine(datasetRoot.Path, "textures/gradient.png"));

        ResoniteMeshVertex sourceA = sourceCityObject.Mesh.Vertices[0];
        ResoniteMeshVertex sourceB = sourceCityObject.Mesh.Vertices[1];
        ResoniteMeshVertex sourceC = sourceCityObject.Mesh.Vertices[2];
        ResoniteMeshVertex atlasA = bakedCityObject.Mesh.Vertices[0];
        ResoniteMeshVertex atlasB = bakedCityObject.Mesh.Vertices[1];
        ResoniteMeshVertex atlasC = bakedCityObject.Mesh.Vertices[2];
        ResoniteMaterialBinding material = sourceCityObject.Materials[0];

        foreach ((double Wa, double Wb, double Wc) weights in new[]
                 {
                     (0.2, 0.3, 0.5),
                     (0.6, 0.2, 0.2),
                     (0.15, 0.7, 0.15),
                 })
        {
            ResoniteFloat2 sourceUv = ApplyWeights(
                ApplyMaterialTransform(sourceA.UV0, material),
                ApplyMaterialTransform(sourceB.UV0, material),
                ApplyMaterialTransform(sourceC.UV0, material),
                weights);
            ResoniteFloat2 atlasUv = ApplyWeights(
                atlasA.UV0,
                atlasB.UV0,
                atlasC.UV0,
                weights);

            Rgba32 expected = SampleWrappedBilinear(sourceImage, sourceUv.X, sourceUv.Y);
            Rgba32 actual = SampleWrappedBilinear(atlasImport, atlasUv.X, atlasUv.Y);
            AssertClose(expected, actual, tolerance: 2);
        }
    }

    [Fact]
    public async Task FlushAllAsyncKeepsCommonMaterialsAsSeparateSubmeshesWhileAtlasingDedicatedAlbedoMaterials()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetImage(datasetRoot.Path, "textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4);
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        ResoniteTextureImportRegistry textureImportRegistry = new();
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            textureImportRegistry,
            maxAtlasSize: 32,
            tilePaddingPixels: 1);

        await AssertBufferedAsync(baker, CreateMixedScopeLod2Building("building-one", "textures/one.png", "unit-a"));

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        ResoniteConstructionCityObject cityObject = Assert.Single(baked);
        Assert.Equal("building-one", cityObject.SlotKey);
        Assert.Equal(2, cityObject.Materials.Count);
        Assert.Equal(2, cityObject.Mesh.Submeshes.Count);
        Assert.Contains(cityObject.Materials, static material => material.AssetScope == ResoniteMaterialAssetScope.Common);
        Assert.Contains(cityObject.Materials, static material => material.TexturePath?.StartsWith("generated/lod2-atlas/", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task FlushAllAsyncCoalescesCommonFacadeAndRoofMaterialsWithinAtlasBatch()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetImage(datasetRoot.Path, "textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4);
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        ResoniteTextureImportRegistry textureImportRegistry = new();
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            textureImportRegistry,
            maxAtlasSize: 64,
            tilePaddingPixels: 1);

        await AssertBufferedAsync(baker, CreateCommonFamilyMixedLod2Building("building-one", "textures/one.png", "unit-a"));

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        ResoniteConstructionCityObject cityObject = Assert.Single(baked);
        Assert.Equal(3, cityObject.Materials.Count);
        Assert.Equal(3, cityObject.Mesh.Submeshes.Count);
        ResoniteMaterialBinding facadeMaterial = Assert.Single(
            cityObject.Materials,
            static material => material.Family == BundledDefaultMaterialFamilies.Facade);
        ResoniteMaterialBinding roofMaterial = Assert.Single(
            cityObject.Materials,
            static material => material.Family == BundledDefaultMaterialFamilies.Roof);
        Assert.Equal(BundledDefaultMaterialFamilies.FacadeVariants[0], facadeMaterial.TexturePath);
        Assert.Equal(BundledDefaultMaterialFamilies.RoofVariants[0], roofMaterial.TexturePath);
        Assert.Equal(ResoniteMaterialAssetScope.Common, facadeMaterial.AssetScope);
        Assert.Equal(ResoniteMaterialAssetScope.Common, roofMaterial.AssetScope);
    }

    [Fact]
    public async Task FlushAllAsyncSplitsSourceUnitWhenAtlasBudgetIsExceeded()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetImage(datasetRoot.Path, "textures/one.png", new Rgba32(255, 0, 0, 255), 12, 12);
        WriteDatasetImage(datasetRoot.Path, "textures/two.png", new Rgba32(0, 255, 0, 255), 12, 12);
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        ResoniteTextureImportRegistry textureImportRegistry = new();
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            textureImportRegistry,
            maxAtlasSize: 16,
            tilePaddingPixels: 1);

        await AssertBufferedAsync(baker, CreateLod2Building("building-one", "textures/one.png", 0, "unit-a"));
        await AssertBufferedAsync(baker, CreateLod2Building("building-two", "textures/two.png", 2, "unit-a"));

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        Assert.Equal(2, baked.Count);
        Assert.All(baked, cityObject =>
        {
            Assert.Single(cityObject.Materials);
            Assert.Single(cityObject.Mesh.Submeshes);
            Assert.Equal("unit-a", cityObject.SourceUnitKey);
        });
    }

    [Fact]
    public async Task FlushAllAsyncFallsBackToOriginalCityObjectWhenSingleCandidateCannotFitAtlasBudget()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetImage(datasetRoot.Path, "textures/one.png", new Rgba32(255, 0, 0, 255), 12, 12);
        WriteDatasetImage(datasetRoot.Path, "textures/two.png", new Rgba32(0, 255, 0, 255), 12, 12);
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        ResoniteTextureImportRegistry textureImportRegistry = new();
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            textureImportRegistry,
            maxAtlasSize: 12,
            tilePaddingPixels: 1);

        ResoniteConstructionCityObject oversizedCandidate = CreateMultiTextureLod2Building("building-one", "textures/one.png", "textures/two.png", "unit-a");
        await AssertBufferedAsync(baker, oversizedCandidate);

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        ResoniteConstructionCityObject cityObject = Assert.Single(baked);
        Assert.Equal(oversizedCandidate.SlotKey, cityObject.SlotKey);
        Assert.Equal(oversizedCandidate.DisplayName, cityObject.DisplayName);
        Assert.Equal(oversizedCandidate.Materials.Count, cityObject.Materials.Count);
        Assert.Equal(oversizedCandidate.Mesh.Submeshes.Count, cityObject.Mesh.Submeshes.Count);
        Assert.DoesNotContain(
            cityObject.Materials,
            static material => material.TexturePath?.StartsWith("generated/lod2-atlas/", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task TryBufferAsyncSkipsNonLod2Objects()
    {
        using TemporaryDirectory datasetRoot = new();
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            new ResoniteTextureImportRegistry());

        BufferedCityObjectBufferResult buffered = await baker.TryBufferAsync(CreateLod2Building("building-one", null, 0, "unit-a") with { LodLevel = 1 });

        Assert.False(buffered.Buffered);
        Assert.Empty(await baker.FlushAllAsync());
    }

    [Fact]
    public async Task TryBufferAsyncSkipsObjectsWithoutAtlasEligibleMaterials()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetImage(datasetRoot.Path, "textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4);
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            new ResoniteTextureImportRegistry());

        BufferedCityObjectBufferResult buffered = await baker.TryBufferAsync(CreateLod2Building(
            "building-one",
            "textures/one.png",
            0,
            "unit-a",
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Family: BundledDefaultMaterialFamilies.Facade));

        Assert.False(buffered.Buffered);
        Assert.Empty(await baker.FlushAllAsync());
    }

    [Fact]
    public async Task TryBufferAsyncBuffersNonBuildingLod2ObjectForPassThroughWhenTextureless()
    {
        using TemporaryDirectory datasetRoot = new();
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            new ResoniteTextureImportRegistry());

        BufferedCityObjectBufferResult buffered = await baker.TryBufferAsync(CreateLod2Vegetation("veg-one", null, 0.0, "unit-a"));

        Assert.True(buffered.Buffered);
        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        ResoniteConstructionCityObject cityObject = Assert.Single(baked);
        Assert.Equal("veg", cityObject.PackageName);
        Assert.Equal("veg-one", cityObject.SlotKey);
        Assert.Single(cityObject.Materials);
        Assert.DoesNotContain(
            cityObject.Materials,
            static material => material.TexturePath?.StartsWith("generated/lod2-atlas/", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task FlushAllAsyncMergesTexturelessNonBuildingMaterialsWithinSameGridCell()
    {
        using TemporaryDirectory datasetRoot = new();
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            new ResoniteTextureImportRegistry());

        await AssertBufferedAsync(baker, CreateLod2Vegetation("veg-one", null, 10.0, "unit-a"));
        await AssertBufferedAsync(baker, CreateLod2Vegetation("veg-two", null, 90.0, "unit-b"));

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        ResoniteConstructionCityObject cityObject = Assert.Single(baked);
        Assert.Equal("veg", cityObject.PackageName);
        Assert.Single(cityObject.Materials);
        Assert.Single(cityObject.Mesh.Submeshes);
        Assert.Equal(6, cityObject.Mesh.Vertices.Count);
        Assert.NotEqual("veg-one", cityObject.SlotKey);
        Assert.NotEqual("veg-two", cityObject.SlotKey);
    }

    [Fact]
    public async Task TryBufferAsyncBuffersNonBuildingLod2OrLaterWithVertexColorMaterial()
    {
        using TemporaryDirectory datasetRoot = new();
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            new ResoniteTextureImportRegistry());

        await AssertBufferedAsync(baker, CreateLod2VegetationVertexColor("veg-two", 10.0, "unit-a"));

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();
        ResoniteConstructionCityObject cityObject = Assert.Single(baked);
        Assert.Equal(ResoniteMaterialType.VertexColor, cityObject.Materials[0].MaterialType);
    }

    [Fact]
    public async Task FlushAllAsyncMergesVertexColorNonBuildingMaterialsWithinSameGridCell()
    {
        using TemporaryDirectory datasetRoot = new();
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            new ResoniteTextureImportRegistry());

        await AssertBufferedAsync(baker, CreateLod2VegetationVertexColor("frn-one", 10.0, "unit-a"));
        await AssertBufferedAsync(baker, CreateLod2VegetationVertexColor("frn-two", 90.0, "unit-b"));

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        ResoniteConstructionCityObject cityObject = Assert.Single(baked);
        Assert.Equal("frn", cityObject.PackageName);
        Assert.Single(cityObject.Materials);
        Assert.Single(cityObject.Mesh.Submeshes);
        Assert.Equal(ResoniteMaterialType.VertexColor, cityObject.Materials[0].MaterialType);
        Assert.Equal(6, cityObject.Mesh.Vertices.Count);
    }

    [Fact]
    public async Task TryBufferAsyncCanAtlasNonBuildingObjectsWhenTextureMaterialIsSuitable()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetImage(datasetRoot.Path, "textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4);
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        ResoniteTextureImportRegistry textureImportRegistry = new();
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            textureImportRegistry,
            maxAtlasSize: 32,
            tilePaddingPixels: 1);

        await AssertBufferedAsync(baker, CreateLod2Vegetation(
            "veg-three",
            "textures/one.png",
            0.0,
            "unit-a"));

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();
        ResoniteConstructionCityObject cityObject = Assert.Single(baked);
        Assert.Single(cityObject.Materials);
        Assert.StartsWith("generated/lod2-atlas/", cityObject.Materials[0].TexturePath!, StringComparison.Ordinal);
        Assert.True(textureImportRegistry.TryGet(cityObject.Materials[0].TexturePath!, cityObject.Materials[0].TextureSourceKind, out ResoniteTextureImport? _));
    }

    [Fact]
    public async Task FlushAllAsyncMergesNonBuildingAtlasCandidatesWithinSameGridCell()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetImage(datasetRoot.Path, "textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4);
        WriteDatasetImage(datasetRoot.Path, "textures/two.png", new Rgba32(0, 255, 0, 255), 4, 4);
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        ResoniteTextureImportRegistry textureImportRegistry = new();
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            textureImportRegistry,
            maxAtlasSize: 32,
            tilePaddingPixels: 1);

        await AssertBufferedAsync(baker, CreateLod2Vegetation("veg-one", "textures/one.png", 10.0, "unit-a"));
        await AssertBufferedAsync(baker, CreateLod2Vegetation("veg-two", "textures/two.png", 90.0, "unit-b"));

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        ResoniteConstructionCityObject cityObject = Assert.Single(baked);
        Assert.Equal("veg", cityObject.PackageName);
        Assert.Single(cityObject.Materials);
        Assert.Single(cityObject.Mesh.Submeshes);
        Assert.StartsWith("generated/lod2-atlas/", cityObject.Materials[0].TexturePath!, StringComparison.Ordinal);
        Assert.NotEqual("veg-one", cityObject.SlotKey);
        Assert.NotEqual("veg-two", cityObject.SlotKey);
        Assert.Contains("_0000", cityObject.SlotKey, StringComparison.Ordinal);
        Assert.True(textureImportRegistry.TryGet(cityObject.Materials[0].TexturePath!, cityObject.Materials[0].TextureSourceKind, out ResoniteTextureImport? _));
    }

    [Fact]
    public async Task FlushAllAsyncUsesDisjointBatchIndicesForMergedPassThroughAndAtlasOutputs()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetImage(datasetRoot.Path, "textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4);
        WriteDatasetImage(datasetRoot.Path, "textures/two.png", new Rgba32(0, 255, 0, 255), 4, 4);
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        ResoniteTextureImportRegistry textureImportRegistry = new();
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            textureImportRegistry,
            maxAtlasSize: 32,
            tilePaddingPixels: 1);

        await AssertBufferedAsync(baker, CreateLod2Vegetation("veg-textured-one", "textures/one.png", 10.0, "unit-a"));
        await AssertBufferedAsync(baker, CreateLod2Vegetation("veg-textured-two", "textures/two.png", 90.0, "unit-b"));
        await AssertBufferedAsync(baker, CreateLod2Vegetation("veg-plain-one", null, 20.0, "unit-c"));
        await AssertBufferedAsync(baker, CreateLod2Vegetation("veg-plain-two", null, 100.0, "unit-d"));

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        Assert.Equal(2, baked.Count);
        Assert.Equal(2, baked.Select(static cityObject => cityObject.SourceObjectKey).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, baked.Select(static cityObject => cityObject.SlotKey).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task FlushAllAsyncMergesNonBuildingAtlasCandidatesAcrossLodScopedBuffers()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetImage(datasetRoot.Path, "textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4);
        WriteDatasetImage(datasetRoot.Path, "textures/two.png", new Rgba32(0, 255, 0, 255), 4, 4);
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        ResoniteTextureImportRegistry textureImportRegistry = new();
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            textureImportRegistry,
            maxAtlasSize: 32,
            tilePaddingPixels: 1);

        BufferedCityObjectBufferResult first = await baker.TryBufferAsync(
            CreateLod2Vegetation("veg-one", "textures/one.png", 10.0, "unit-a"));
        BufferedCityObjectBufferResult second = await baker.TryBufferAsync(
            CreateLod2Vegetation("veg-two", "textures/two.png", 200.0, "unit-b"));

        List<ResoniteConstructionCityObject> baked = [.. first.ReadyCityObjects, .. second.ReadyCityObjects];
        baked.AddRange(await baker.FlushAllAsync());

        Assert.Single(baked);
        Assert.All(
            baked,
            cityObject => Assert.StartsWith("generated/lod2-atlas/", cityObject.Materials[0].TexturePath!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TryBufferAsyncBuffersNonBuildingLod3Object()
    {
        using TemporaryDirectory datasetRoot = new();
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            new ResoniteTextureImportRegistry());

        await AssertBufferedAsync(baker, CreateLod2Vegetation("veg-three", null, 0.0, "unit-a") with { LodLevel = 3 });

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();
        Assert.Single(baked);
    }

    [Fact]
    public async Task TryBufferAsyncBuffersNonBuildingLod2ObjectForUnclassifiedPackage()
    {
        using TemporaryDirectory datasetRoot = new();
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            new ResoniteTextureImportRegistry());

        await AssertBufferedAsync(
            baker,
            CreateLod2Vegetation("wtr-one", null, 0.0, "unit-a") with { PackageName = "wtr" });

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        ResoniteConstructionCityObject cityObject = Assert.Single(baked);
        Assert.Equal("wtr", cityObject.PackageName);
        Assert.Single(cityObject.Materials);
        Assert.DoesNotContain(
            "generated/lod2-atlas/",
            cityObject.Materials[0].TexturePath ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryBufferAsyncBuffersBuildingLod3Object()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetImage(datasetRoot.Path, "textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4);
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        ResoniteTextureImportRegistry textureImportRegistry = new();
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            textureImportRegistry,
            maxAtlasSize: 32,
            tilePaddingPixels: 1);

        await AssertBufferedAsync(
            baker,
            CreateLod2Building("building-three", "textures/one.png", 0, "unit-a") with { LodLevel = 3 });

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        ResoniteConstructionCityObject cityObject = Assert.Single(baked);
        Assert.Equal("bldg", cityObject.PackageName);
        Assert.StartsWith("generated/lod2-atlas/", cityObject.Materials[0].TexturePath!, StringComparison.Ordinal);
        Assert.True(textureImportRegistry.TryGet(cityObject.Materials[0].TexturePath!, cityObject.Materials[0].TextureSourceKind, out ResoniteTextureImport? _));
    }

    [Fact]
    public async Task TryBufferAsyncCanEmitReadyCityObjectsBeforeCompletion()
    {
        using TemporaryDirectory datasetRoot = new();
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            new ResoniteTextureImportRegistry(),
            maxBufferedCityObjectsPerSourceUnit: 1);

        BufferedCityObjectBufferResult first = await baker.TryBufferAsync(
            CreateLod2Vegetation("veg-one", null, 10.0, "unit-a"));
        BufferedCityObjectBufferResult second = await baker.TryBufferAsync(
            CreateLod2Vegetation("veg-two", null, 90.0, "unit-a"));

        Assert.True(first.Buffered);
        Assert.Empty(first.ReadyCityObjects);
        Assert.True(second.Buffered);
        Assert.Empty(second.ReadyCityObjects);

        IReadOnlyList<ResoniteConstructionCityObject> flushed = await baker.FlushAllAsync();
        Assert.Single(flushed);
    }

    [Fact]
    public async Task TryBufferAsyncDoesNotFlushWhenSourceUnitChangesWithoutCapacityPressure()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetImage(datasetRoot.Path, "textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4);
        WriteDatasetImage(datasetRoot.Path, "textures/two.png", new Rgba32(0, 255, 0, 255), 4, 4);
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            new ResoniteTextureImportRegistry(),
            maxAtlasSize: 32,
            tilePaddingPixels: 1);

        BufferedCityObjectBufferResult first = await baker.TryBufferAsync(
            CreateLod2Building("building-one", "textures/one.png", 10.0, "unit-a"));
        BufferedCityObjectBufferResult second = await baker.TryBufferAsync(
            CreateLod2Building("building-two", "textures/two.png", 20.0, "unit-b"));

        Assert.True(first.Buffered);
        Assert.Empty(first.ReadyCityObjects);
        Assert.True(second.Buffered);
        Assert.Empty(second.ReadyCityObjects);

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();
        Assert.Single(baked);
    }

    [Fact]
    public async Task TryBufferAsyncReturnsFlushedCityObjectForExceededSourceUnitLimit()
    {
        using TemporaryDirectory datasetRoot = new();
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            new ResoniteTextureImportRegistry(),
            maxBufferedSourceUnits: 1);

        BufferedCityObjectBufferResult first = await baker.TryBufferAsync(
            CreateLod2Vegetation("veg-one", null, 10.0, "unit-a"));
        BufferedCityObjectBufferResult second = await baker.TryBufferAsync(
            CreateLod2Vegetation("veg-two", null, 20.0, "unit-b"));
        BufferedCityObjectBufferResult third = await baker.TryBufferAsync(
            CreateLod2Vegetation("veg-three", null, 30.0, "unit-c"));

        Assert.True(first.Buffered);
        Assert.Empty(first.ReadyCityObjects);
        Assert.True(second.Buffered);
        Assert.Empty(second.ReadyCityObjects);
        Assert.True(third.Buffered);
        Assert.Empty(third.ReadyCityObjects);

        IReadOnlyList<ResoniteConstructionCityObject> remaining = await baker.FlushAllAsync();
        Assert.Single(remaining);
    }

    [Fact]
    public async Task FlushAllAsyncSeparatesLodsAcrossInterleavedInputs()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetImage(datasetRoot.Path, "textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4);
        WriteDatasetImage(datasetRoot.Path, "textures/two.png", new Rgba32(0, 255, 0, 255), 4, 4);
        WriteDatasetImage(datasetRoot.Path, "textures/three.png", new Rgba32(0, 0, 255, 255), 4, 4);
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            new ResoniteTextureImportRegistry(),
            maxAtlasSize: 32,
            tilePaddingPixels: 1,
            maxBufferedSourceUnits: 8);

        await baker.TryBufferAsync(CreateLod2Building("a-one", "textures/one.png", 10.0, "unit-a") with
        {
            SourceFileRelativePath = "udx/bldg/53394525_citygml/a.gml",
        });
        await baker.TryBufferAsync(CreateLod2Building("b-one", "textures/two.png", 20.0, "unit-b") with
        {
            SourceFileRelativePath = "udx/bldg/53394525_citygml/b.gml",
            LodLevel = 3,
        });
        await baker.TryBufferAsync(CreateLod2Building("a-two", "textures/three.png", 30.0, "unit-a") with
        {
            SourceFileRelativePath = "udx/bldg/53394525_citygml/c.gml",
        });

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        Assert.Equal(2, baked.Count);
        Assert.Contains(baked, static cityObject => cityObject.LodLevel == 2);
        Assert.Contains(baked, static cityObject => cityObject.LodLevel == 3);
    }

    [Fact]
    public async Task FlushAllAsyncSeparatesGridPassThroughCandidatesAcrossSourceFiles()
    {
        using TemporaryDirectory datasetRoot = new();
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            new ResoniteTextureImportRegistry());

        await baker.TryBufferAsync(CreateLod2Vegetation("veg-one", null, 10.0, "shared-unit") with
        {
            SourceFileRelativePath = "udx/veg/53394525_citygml/a.gml",
        });
        await baker.TryBufferAsync(CreateLod2Vegetation("veg-two", null, 20.0, "shared-unit") with
        {
            SourceFileRelativePath = "udx/veg/53394525_citygml/b.gml",
        });

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        Assert.Single(baked);
        Assert.Equal("shared-unit", baked[0].SourceUnitKey);
        Assert.Equal("udx/veg/53394525_citygml/a.gml", baked[0].SourceFileRelativePath);
    }

    private static async Task AssertBufferedAsync(
        Lod2AtlasCityObjectBaker baker,
        ResoniteConstructionCityObject cityObject)
    {
        BufferedCityObjectBufferResult result = await baker.TryBufferAsync(cityObject);
        Assert.True(result.Buffered);
    }

    private static ResoniteConstructionCityObject CreateLod2Building(
        string slotKey,
        string? texturePath,
        double x,
        string sourceUnitKey,
        ResoniteTextureSourceKind TextureSourceKind = ResoniteTextureSourceKind.Dataset,
        string? Family = null)
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
                    new ResoniteMeshSubmesh(0, $"{slotKey}-material", [0, 1, 2]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: texturePath,
                    TextureSourceKind: TextureSourceKind,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    Family: Family)
            ],
            SourceObjectKey: $"{sourceUnitKey}:{slotKey}",
            SourceUnitKey: sourceUnitKey,
            SourceFileRelativePath: $"{sourceUnitKey}.gml");
    }

    private static ResoniteConstructionCityObject CreateLod2Vegetation(
        string slotKey,
        string? texturePath,
        double x,
        string sourceUnitKey)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: slotKey,
            PackageName: "veg",
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
                    new ResoniteMeshSubmesh(0, $"{slotKey}-material", [0, 1, 2]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: texturePath,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: $"{sourceUnitKey}:{slotKey}",
            SourceUnitKey: sourceUnitKey,
            SourceFileRelativePath: $"{sourceUnitKey}.gml");
    }

    private static ResoniteConstructionCityObject CreateLod2VegetationVertexColor(
        string slotKey,
        double x,
        string sourceUnitKey)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: slotKey,
            PackageName: "frn",
            ActualMeshCode: "53394525",
            LodLevel: 2,
            Transform: new ResoniteTransform(new ResoniteFloat3(x, 0.0, 0.0)),
            Mesh: new ResoniteImportedMesh(
                [
                    new ResoniteMeshVertex(
                        new ResoniteFloat3(0.0, 0.0, 0.0),
                        new ResoniteFloat3(0.0, 1.0, 0.0),
                        new ResoniteFloat2(0.0, 0.0),
                        new ResoniteColor(1.0, 0.0, 0.0, 1.0)),
                    new ResoniteMeshVertex(
                        new ResoniteFloat3(1.0, 0.0, 0.0),
                        new ResoniteFloat3(0.0, 1.0, 0.0),
                        new ResoniteFloat2(1.0, 0.0),
                        new ResoniteColor(0.0, 1.0, 0.0, 1.0)),
                    new ResoniteMeshVertex(
                        new ResoniteFloat3(0.0, 1.0, 0.0),
                        new ResoniteFloat3(0.0, 1.0, 0.0),
                        new ResoniteFloat2(0.0, 1.0),
                        new ResoniteColor(0.0, 0.0, 1.0, 1.0)),
                ],
                [
                    new ResoniteMeshSubmesh(0, $"{slotKey}-material", [0, 1, 2]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.VertexColor,
                    TexturePath: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: $"{sourceUnitKey}:{slotKey}",
            SourceUnitKey: sourceUnitKey,
            SourceFileRelativePath: $"{sourceUnitKey}.gml");
    }

    private static ResoniteConstructionCityObject CreateMultiTextureLod2Building(
        string slotKey,
        string firstTexturePath,
        string secondTexturePath,
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
                    new ResoniteMeshSubmesh(0, $"{slotKey}-material-0", [0, 1, 2]),
                    new ResoniteMeshSubmesh(1, $"{slotKey}-material-1", [3, 4, 5]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-material-0",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: firstTexturePath,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-material-1",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: secondTexturePath,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1]),
            ],
            SourceObjectKey: $"{sourceUnitKey}:{slotKey}",
            SourceUnitKey: sourceUnitKey,
            SourceFileRelativePath: $"{sourceUnitKey}.gml");
    }

    private static ResoniteConstructionCityObject CreateMixedScopeLod2Building(
        string slotKey,
        string texturePath,
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
                    new ResoniteMeshSubmesh(0, $"{slotKey}-material-0", [0, 1, 2]),
                    new ResoniteMeshSubmesh(1, $"{slotKey}-material-1", [1, 3, 2]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-material-0",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: texturePath,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
                new ResoniteMaterialBinding(
                    MaterialKey: "shared-bottom",
                    BaseColor: new ResoniteColor(0.4, 0.4, 0.4, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    AssetScope: ResoniteMaterialAssetScope.Common),
            ],
            SourceObjectKey: $"{sourceUnitKey}:{slotKey}",
            SourceUnitKey: sourceUnitKey,
            SourceFileRelativePath: $"{sourceUnitKey}.gml");
    }

    private static ResoniteConstructionCityObject CreateUvScaledLod2Building(
        string slotKey,
        string texturePath,
        string sourceUnitKey,
        ResoniteFloat2 textureScale,
        ResoniteFloat2? textureOffset)
    {
        return CreateLod2Building(
            slotKey,
            texturePath,
            0.0,
            sourceUnitKey) with
        {
            Materials =
            [
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: texturePath,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    TextureScale: textureScale,
                    TextureOffset: textureOffset)
            ],
        };
    }

    private static ResoniteConstructionCityObject CreateCommonFamilyMixedLod2Building(
        string slotKey,
        string texturePath,
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
                    new ResoniteMeshVertex(new ResoniteFloat3(2.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(3.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(2.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(4.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(3.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(5.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(4.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                ],
                [
                    new ResoniteMeshSubmesh(0, $"{slotKey}-material-0", [0, 1, 2]),
                    new ResoniteMeshSubmesh(1, $"{slotKey}-facade-a", [1, 3, 4]),
                    new ResoniteMeshSubmesh(2, $"{slotKey}-facade-b", [3, 5, 6]),
                    new ResoniteMeshSubmesh(3, $"{slotKey}-roof-a", [5, 7, 8]),
                    new ResoniteMeshSubmesh(4, $"{slotKey}-roof-b", [7, 9, 10]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-material-0",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: texturePath,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-facade-a",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: BundledDefaultMaterialFamilies.FacadeVariants[1],
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    TextureScale: BundledDefaultMaterialProfiles.GetTilesPerMeter(BundledDefaultMaterialFamilies.FacadeVariants[1]),
                    Family: BundledDefaultMaterialFamilies.Facade,
                    AssetScope: ResoniteMaterialAssetScope.Common),
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-facade-b",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: BundledDefaultMaterialFamilies.FacadeVariants[2],
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [2],
                    TextureScale: BundledDefaultMaterialProfiles.GetTilesPerMeter(BundledDefaultMaterialFamilies.FacadeVariants[2]),
                    Family: BundledDefaultMaterialFamilies.Facade,
                    AssetScope: ResoniteMaterialAssetScope.Common),
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-roof-a",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: BundledDefaultMaterialFamilies.RoofVariants[2],
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [3],
                    TextureScale: BundledDefaultMaterialProfiles.GetTilesPerMeter(BundledDefaultMaterialFamilies.RoofVariants[2]),
                    Family: BundledDefaultMaterialFamilies.Roof,
                    AssetScope: ResoniteMaterialAssetScope.Common),
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-roof-b",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: BundledDefaultMaterialFamilies.RoofVariants[3],
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [4],
                    TextureScale: BundledDefaultMaterialProfiles.GetTilesPerMeter(BundledDefaultMaterialFamilies.RoofVariants[3]),
                    Family: BundledDefaultMaterialFamilies.Roof,
                    AssetScope: ResoniteMaterialAssetScope.Common),
            ],
            SourceObjectKey: $"{sourceUnitKey}:{slotKey}",
            SourceUnitKey: sourceUnitKey,
            SourceFileRelativePath: $"{sourceUnitKey}.gml");
    }

    private static void WriteDatasetImage(string datasetRoot, string relativePath, Rgba32 color, int width, int height)
    {
        string absolutePath = Path.Combine(datasetRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        using Image<Rgba32> image = new(width, height, color);
        image.SaveAsPng(absolutePath);
    }

    private static void WriteStripedDatasetImage(string datasetRoot, string relativePath, IReadOnlyList<Rgba32> colors)
    {
        string absolutePath = Path.Combine(datasetRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        using Image<Rgba32> image = new(colors.Count, 1);
        for (int x = 0; x < colors.Count; x++)
        {
            image[x, 0] = colors[x];
        }

        image.SaveAsPng(absolutePath);
    }

    private static void WriteGradientDatasetImage(string datasetRoot, string relativePath, int width, int height)
    {
        string absolutePath = Path.Combine(datasetRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        using Image<Rgba32> image = new(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                image[x, y] = new Rgba32(
                    (byte)(x * 29),
                    (byte)(y * 31),
                    (byte)((x * 17) + (y * 13)),
                    255);
            }
        }

        image.SaveAsPng(absolutePath);
    }

    private static Rgba32 ReadPixel(ResoniteRawTextureImport textureImport, int x, int y)
    {
        int offset = ((y * textureImport.Width) + x) * 4;
        return new Rgba32(
            textureImport.RawRgba32Bytes[offset],
            textureImport.RawRgba32Bytes[offset + 1],
            textureImport.RawRgba32Bytes[offset + 2],
            textureImport.RawRgba32Bytes[offset + 3]);
    }

    private static ResoniteFloat2 ApplyMaterialTransform(ResoniteFloat2 uv, ResoniteMaterialBinding material)
    {
        return new ResoniteFloat2(
            (uv.X * (material.TextureScale?.X ?? 1.0)) + (material.TextureOffset?.X ?? 0.0),
            (uv.Y * (material.TextureScale?.Y ?? 1.0)) + (material.TextureOffset?.Y ?? 0.0));
    }

    private static ResoniteFloat2 ApplyWeights(
        ResoniteFloat2 a,
        ResoniteFloat2 b,
        ResoniteFloat2 c,
        (double Wa, double Wb, double Wc) weights)
    {
        return new ResoniteFloat2(
            (a.X * weights.Wa) + (b.X * weights.Wb) + (c.X * weights.Wc),
            (a.Y * weights.Wa) + (b.Y * weights.Wb) + (c.Y * weights.Wc));
    }

    private static Rgba32 SampleWrappedBilinear(Image<Rgba32> image, double u, double v)
    {
        return SampleWrappedBilinear(
            image.Width,
            image.Height,
            (x, y) => image[x, y],
            u,
            v);
    }

    private static Rgba32 SampleWrappedBilinear(ResoniteRawTextureImport image, double u, double v)
    {
        return SampleWrappedBilinear(
            image.Width,
            image.Height,
            (x, y) => ReadPixel(image, x, y),
            u,
            v);
    }

    private static Rgba32 SampleWrappedBilinear(
        int width,
        int height,
        Func<int, int, Rgba32> readPixel,
        double u,
        double v)
    {
        double wrappedU = WrapUv(u);
        double wrappedV = WrapUv(v);
        double sourceX = (wrappedU * width) - 0.5;
        double sourceY = ((1.0 - wrappedV) * height) - 0.5;
        int x0 = (int)Math.Floor(sourceX);
        int y0 = (int)Math.Floor(sourceY);
        int x1 = x0 + 1;
        int y1 = y0 + 1;
        double tx = sourceX - x0;
        double ty = sourceY - y0;

        Rgba32 topLeft = readPixel(WrapCoordinate(x0, width), WrapCoordinate(y0, height));
        Rgba32 topRight = readPixel(WrapCoordinate(x1, width), WrapCoordinate(y0, height));
        Rgba32 bottomLeft = readPixel(WrapCoordinate(x0, width), WrapCoordinate(y1, height));
        Rgba32 bottomRight = readPixel(WrapCoordinate(x1, width), WrapCoordinate(y1, height));
        return new Rgba32(
            LerpChannel(topLeft.R, topRight.R, bottomLeft.R, bottomRight.R, tx, ty),
            LerpChannel(topLeft.G, topRight.G, bottomLeft.G, bottomRight.G, tx, ty),
            LerpChannel(topLeft.B, topRight.B, bottomLeft.B, bottomRight.B, tx, ty),
            LerpChannel(topLeft.A, topRight.A, bottomLeft.A, bottomRight.A, tx, ty));
    }

    private static double WrapUv(double value)
    {
        double wrapped = value - Math.Floor(value);
        return wrapped >= 1.0 ? 0.0 : wrapped;
    }

    private static int WrapCoordinate(int value, int length)
    {
        int wrapped = value % length;
        return wrapped < 0 ? wrapped + length : wrapped;
    }

    private static byte LerpChannel(
        byte topLeft,
        byte topRight,
        byte bottomLeft,
        byte bottomRight,
        double tx,
        double ty)
    {
        double top = topLeft + ((topRight - topLeft) * tx);
        double bottom = bottomLeft + ((bottomRight - bottomLeft) * tx);
        double value = top + ((bottom - top) * ty);
        return (byte)Math.Round(Math.Clamp(value, 0.0, 255.0));
    }

    private static void AssertClose(Rgba32 expected, Rgba32 actual, byte tolerance)
    {
        Assert.InRange(Math.Abs(expected.R - actual.R), 0, tolerance);
        Assert.InRange(Math.Abs(expected.G - actual.G), 0, tolerance);
        Assert.InRange(Math.Abs(expected.B - actual.B), 0, tolerance);
        Assert.InRange(Math.Abs(expected.A - actual.A), 0, tolerance);
    }

    private sealed class FakeDatasetContentSource(string sourceRoot) : IPlateauDatasetContentSource
    {
        public string SourcePath => sourceRoot;

        public IReadOnlyList<string> EnumerateFiles()
        {
            return Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'))
                .ToArray();
        }

        public bool FileExists(string relativePath)
        {
            return File.Exists(Path.Combine(sourceRoot, relativePath));
        }

        public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<Stream>(new FileStream(
                Path.Combine(sourceRoot, relativePath),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read));
        }

        public Task<string> MaterializeFileAsync(string relativePath, string outputRoot, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string sourcePath = Path.Combine(sourceRoot, relativePath);
            string destinationPath = Path.Combine(outputRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
            return Task.FromResult(destinationPath);
        }
    }
}
