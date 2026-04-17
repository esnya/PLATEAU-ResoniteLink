using Plateau.ResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Plateau.ResoniteLink.Tests.Targets;

public sealed class Lod2AtlasCityObjectBakerTests
{
    [Fact]
    public async Task FlushAllAsyncBakesSingleSourceUnitIntoSingleMaterialAndSubmesh()
    {
        Lod2AtlasCityObjectBaker baker = new(new ResoniteTextureImageLoader(), maxAtlasSize: 32, tilePaddingPixels: 1);

        await AssertBufferedAsync(baker, CreateLod2Building("building-one", CreatePayload("textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4), 0, "unit-a"));
        await AssertBufferedAsync(baker, CreateLod2Building("building-two", CreatePayload("textures/two.png", new Rgba32(0, 255, 0, 255), 4, 4), 2, "unit-a"));

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        ResoniteConstructionCityObject cityObject = Assert.Single(baked);
        Assert.Single(cityObject.Materials);
        Assert.Single(cityObject.Mesh.Submeshes);
        Assert.Equal(6, cityObject.Mesh.Vertices.Count);
        Assert.Equal("unit-a", cityObject.SourceUnitKey);
        ResoniteTexturePayload atlasPayload = Assert.IsType<ResoniteTexturePayload>(cityObject.Materials[0].TexturePayload);
        Assert.Equal(ResoniteTexturePayloadFormat.RawRgba32, atlasPayload.Format);
        Assert.NotNull(atlasPayload.Width);
        Assert.NotNull(atlasPayload.Height);
        Assert.InRange(atlasPayload.Width!.Value, 1, 32);
        Assert.InRange(atlasPayload.Height!.Value, 1, 32);
    }

    [Fact]
    public async Task FlushAllAsyncBakesActualUsedUvRegionInsteadOfWholeSourceTexture()
    {
        Lod2AtlasCityObjectBaker baker = new(new ResoniteTextureImageLoader(), maxAtlasSize: 32, tilePaddingPixels: 0);

        await AssertBufferedAsync(baker, CreateUvScaledLod2Building(
            "building-one",
            CreateStripedPayload("textures/striped.png", [new Rgba32(255, 0, 0, 255), new Rgba32(0, 255, 0, 255), new Rgba32(0, 0, 255, 255), new Rgba32(255, 255, 0, 255)]),
            "unit-a",
            new ResoniteFloat2(0.25, 1.0),
            new ResoniteFloat2(0.5, 0.0)));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        ResoniteTexturePayload atlasPayload = Assert.IsType<ResoniteTexturePayload>(cityObject.Materials[0].TexturePayload);
        Assert.Equal(ResoniteTexturePayloadFormat.RawRgba32, atlasPayload.Format);
        Assert.Equal(1, atlasPayload.Width);
        Assert.Equal(1, atlasPayload.Height);
        Assert.Equal(new Rgba32(0, 0, 255, 255), ReadPixel(atlasPayload, 0, 0));
    }

    [Fact]
    public async Task FlushAllAsyncRepeatsTextureContentWhenUsedUvRangeExceedsUnitSquare()
    {
        Lod2AtlasCityObjectBaker baker = new(new ResoniteTextureImageLoader(), maxAtlasSize: 32, tilePaddingPixels: 0);

        await AssertBufferedAsync(baker, CreateUvScaledLod2Building(
            "building-one",
            CreateStripedPayload("textures/repeat.png", [new Rgba32(255, 0, 0, 255), new Rgba32(0, 255, 0, 255)]),
            "unit-a",
            new ResoniteFloat2(2.0, 1.0),
            null));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        ResoniteTexturePayload atlasPayload = Assert.IsType<ResoniteTexturePayload>(cityObject.Materials[0].TexturePayload);
        Assert.Equal(ResoniteTexturePayloadFormat.RawRgba32, atlasPayload.Format);
        Assert.Equal(4, atlasPayload.Width);
        Assert.Equal(1, atlasPayload.Height);
        Assert.Equal(new Rgba32(255, 0, 0, 255), ReadPixel(atlasPayload, 0, 0));
        Assert.Equal(new Rgba32(0, 255, 0, 255), ReadPixel(atlasPayload, 1, 0));
        Assert.Equal(new Rgba32(255, 0, 0, 255), ReadPixel(atlasPayload, 2, 0));
        Assert.Equal(new Rgba32(0, 255, 0, 255), ReadPixel(atlasPayload, 3, 0));
    }

    [Fact]
    public async Task FlushAllAsyncKeepsCommonMaterialsAsSeparateSubmeshesWhileAtlasingDedicatedMaterials()
    {
        Lod2AtlasCityObjectBaker baker = new(new ResoniteTextureImageLoader(), maxAtlasSize: 32, tilePaddingPixels: 1);

        await AssertBufferedAsync(baker, CreateMixedScopeLod2Building("building-one", CreatePayload("textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4), "unit-a"));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        Assert.Equal(2, cityObject.Materials.Count);
        Assert.Equal(2, cityObject.Mesh.Submeshes.Count);
        Assert.Contains(cityObject.Materials, static material => material.AssetScope == ResoniteMaterialAssetScope.Common);
        Assert.Contains(cityObject.Materials, static material => material.TexturePayload is not null);
    }

    [Fact]
    public async Task FlushAllAsyncPreservesDistinctCommonMaterialVariants()
    {
        Lod2AtlasCityObjectBaker baker = new(new ResoniteTextureImageLoader(), maxAtlasSize: 32, tilePaddingPixels: 1);

        await AssertBufferedAsync(
            baker,
            CreateCommonVariantMixedLod2Building(
                "building-one",
                CreatePayload("textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4),
                "unit-a"));

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        ResoniteMaterialBinding[] commonMaterials = cityObject.Materials
            .Where(static material => material.AssetScope == ResoniteMaterialAssetScope.Common)
            .OrderBy(static material => material.BundledVariantIndex)
            .ToArray();

        Assert.Equal(3, cityObject.Materials.Count);
        Assert.Equal(3, cityObject.Mesh.Submeshes.Count);
        Assert.Equal(2, commonMaterials.Length);
        Assert.Equal(BundledDefaultMaterialFamilies.Facade, commonMaterials[0].Family);
        Assert.Equal(BundledDefaultMaterialFamilies.Facade, commonMaterials[1].Family);
        Assert.Equal(0, commonMaterials[0].BundledVariantIndex);
        Assert.Equal(1, commonMaterials[1].BundledVariantIndex);
        Assert.NotEqual(commonMaterials[0].MaterialKey, commonMaterials[1].MaterialKey);
    }

    [Fact]
    public async Task FlushAllAsyncFallsBackToOriginalCityObjectWhenSingleCandidateCannotFitAtlasBudget()
    {
        Lod2AtlasCityObjectBaker baker = new(new ResoniteTextureImageLoader(), maxAtlasSize: 12, tilePaddingPixels: 1);
        ResoniteConstructionCityObject oversizedCandidate = CreateMultiTextureLod2Building(
            "building-one",
            CreatePayload("textures/one.png", new Rgba32(255, 0, 0, 255), 12, 12),
            CreatePayload("textures/two.png", new Rgba32(0, 255, 0, 255), 12, 12),
            "unit-a");

        await AssertBufferedAsync(baker, oversizedCandidate);

        ResoniteConstructionCityObject cityObject = Assert.Single(await baker.FlushAllAsync());
        Assert.Equal(oversizedCandidate.SlotKey, cityObject.SlotKey);
        Assert.Equal(oversizedCandidate.DisplayName, cityObject.DisplayName);
        Assert.Equal(oversizedCandidate.Materials.Count, cityObject.Materials.Count);
        Assert.All(cityObject.Materials, static material => Assert.NotNull(material.TexturePayload));
        Assert.DoesNotContain(cityObject.Materials, static material => material.TexturePayload?.Identity?.Contains("generated/lod2-atlas/", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task FlushAllAsyncKeepsDistinctSourceUnitsInSeparateAtlasBatches()
    {
        Lod2AtlasCityObjectBaker baker = new(new ResoniteTextureImageLoader(), maxAtlasSize: 32, tilePaddingPixels: 1);

        await AssertBufferedAsync(baker, CreateLod2Building("building-one", CreatePayload("textures/one.png", new Rgba32(255, 0, 0, 255), 4, 4), 0, "unit-a"));
        await AssertBufferedAsync(baker, CreateLod2Building("building-two", CreatePayload("textures/two.png", new Rgba32(0, 255, 0, 255), 4, 4), 2, "unit-b"));

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        Assert.Equal(2, baked.Count);
        Assert.Contains(baked, static cityObject => cityObject.SourceUnitKey == "unit-a" && cityObject.SourceFileRelativePath == "unit-a.gml");
        Assert.Contains(baked, static cityObject => cityObject.SourceUnitKey == "unit-b" && cityObject.SourceFileRelativePath == "unit-b.gml");
    }

    private static async Task AssertBufferedAsync(Lod2AtlasCityObjectBaker baker, ResoniteConstructionCityObject cityObject)
    {
        BufferedCityObjectBufferResult result = await baker.TryBufferAsync(cityObject);
        Assert.True(result.Buffered);
        Assert.Empty(result.ReadyCityObjects);
    }

    private static ResoniteTexturePayload CreatePayload(string identity, Rgba32 color, int width, int height)
    {
        using Image<Rgba32> image = new(width, height, color);
        return ResoniteTextureImportFactory.CreatePayloadFromImage(image, identity: identity);
    }

    private static ResoniteTexturePayload CreateStripedPayload(string identity, IReadOnlyList<Rgba32> colors)
    {
        using Image<Rgba32> image = new(colors.Count, 1);
        for (int x = 0; x < colors.Count; x++)
        {
            image[x, 0] = colors[x];
        }

        return ResoniteTextureImportFactory.CreatePayloadFromImage(image, identity: identity);
    }

    private static Rgba32 ReadPixel(ResoniteTexturePayload payload, int x, int y)
    {
        Assert.Equal(ResoniteTexturePayloadFormat.RawRgba32, payload.Format);
        Assert.NotNull(payload.Width);
        int width = payload.Width.Value;
        int offset = ((y * width) + x) * 4;
        return new Rgba32(
            payload.BinaryPayload[offset],
            payload.BinaryPayload[offset + 1],
            payload.BinaryPayload[offset + 2],
            payload.BinaryPayload[offset + 3]);
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
                    new ResoniteMeshSubmesh(0, $"{slotKey}-material", [0, 1, 2]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: payload,
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
                    new ResoniteMeshSubmesh(0, $"{slotKey}-material-0", [0, 1, 2]),
                    new ResoniteMeshSubmesh(1, $"{slotKey}-material-1", [3, 4, 5]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-material-0",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: firstPayload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-material-1",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: secondPayload,
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
                    new ResoniteMeshSubmesh(0, $"{slotKey}-material-0", [0, 1, 2]),
                    new ResoniteMeshSubmesh(1, $"{slotKey}-material-1", [1, 3, 2]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-material-0",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: payload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
                new ResoniteMaterialBinding(
                    MaterialKey: "shared-bottom",
                    BaseColor: new ResoniteColor(0.4, 0.4, 0.4, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    Family: BundledDefaultMaterialFamilies.Facade,
                    AssetScope: ResoniteMaterialAssetScope.Common,
                    BundledVariantIndex: 0),
            ],
            SourceObjectKey: $"{sourceUnitKey}:{slotKey}",
            SourceUnitKey: sourceUnitKey,
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
                    MaterialKey: $"{slotKey}-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: payload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    TextureScale: textureScale,
                    TextureOffset: textureOffset),
            ],
        };
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
                    new ResoniteMeshSubmesh(0, $"{slotKey}-atlas", [0, 1, 2]),
                    new ResoniteMeshSubmesh(1, $"{slotKey}-common-0", [3, 4, 5]),
                    new ResoniteMeshSubmesh(2, $"{slotKey}-common-1", [6, 7, 8]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-atlas",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: payload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-common-0",
                    BaseColor: new ResoniteColor(0.5, 0.5, 0.5, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    Family: BundledDefaultMaterialFamilies.Facade,
                    AssetScope: ResoniteMaterialAssetScope.Common,
                    BundledVariantIndex: 0),
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-common-1",
                    BaseColor: new ResoniteColor(0.5, 0.5, 0.5, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [2],
                    Family: BundledDefaultMaterialFamilies.Facade,
                    AssetScope: ResoniteMaterialAssetScope.Common,
                    BundledVariantIndex: 1),
            ],
            SourceObjectKey: $"{sourceUnitKey}:{slotKey}",
            SourceUnitKey: sourceUnitKey,
            SourceFileRelativePath: $"{sourceUnitKey}.gml");
    }
}
