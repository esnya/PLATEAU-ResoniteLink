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

        Assert.True(await baker.TryBufferAsync(CreateLod2Building("building-one", "textures/one.png", 0, "unit-a")));
        Assert.True(await baker.TryBufferAsync(CreateLod2Building("building-two", "textures/two.png", 2, "unit-a")));

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

        Assert.True(await baker.TryBufferAsync(CreateLod2Building("building-one", "textures/one.png", 0, "unit-a")));
        Assert.True(await baker.TryBufferAsync(CreateLod2Building("building-two", "textures/two.png", 2, "unit-a")));

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
    public async Task TryBufferAsyncSkipsNonLod2Objects()
    {
        using TemporaryDirectory datasetRoot = new();
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        Lod2AtlasCityObjectBaker baker = new(
            new ResoniteTextureImageLoader(datasetContentSource),
            new ResoniteTextureImportRegistry());

        bool buffered = await baker.TryBufferAsync(CreateLod2Building("building-one", null, 0, "unit-a") with { LodLevel = 1 });

        Assert.False(buffered);
        Assert.Empty(await baker.FlushAllAsync());
    }

    private static ResoniteConstructionCityObject CreateLod2Building(
        string slotKey,
        string? texturePath,
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
                    TexturePath: texturePath,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0])
            ],
            SourceObjectKey: $"{sourceUnitKey}:{slotKey}",
            SourceUnitKey: sourceUnitKey);
    }

    private static void WriteDatasetImage(string datasetRoot, string relativePath, Rgba32 color, int width, int height)
    {
        string absolutePath = Path.Combine(datasetRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        using Image<Rgba32> image = new(width, height, color);
        image.SaveAsPng(absolutePath);
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
