using System;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Application;

public sealed class ThirdRegionalMeshCodeBoundaryTests
{
    [Fact]
    public void TerrainTextureOverlayRejectsNullMeshCodeAtConstruction()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TerrainTextureOverlay(
                PackageName: "dem",
                MeshCode: null!,
                GeographicBounds: CreateBounds(),
                MaxTextureSize: 512,
                Sources: [CreateTileSource()]));
    }

    [Fact]
    public void TerrainTextureOverlayRejectsNullMeshCodeInWithExpression()
    {
        TerrainTextureOverlay overlay = CreateOverlay();

        Assert.Throws<ArgumentNullException>(() => overlay with { MeshCode = null! });
    }

    [Fact]
    public void TerrainOverlayMaterialBindingRejectsNullMeshCodeAtConstruction()
    {
        TerrainTextureOverlay overlay = CreateOverlay();

        Assert.Throws<ArgumentNullException>(() => new TerrainOverlayMaterialBinding(null!, overlay));
    }

    [Fact]
    public void TerrainOverlayMaterialBindingRejectsNullMeshCodeInWithExpression()
    {
        TerrainOverlayMaterialBinding binding = new(ThirdRegionalMeshCode.Parse("53394525"), CreateOverlay());

        Assert.Throws<ArgumentNullException>(() => binding with { MeshCode = null! });
    }

    [Fact]
    public void DemTerrainOverlayRegionRejectsNullMeshCodeAtConstruction()
    {
        Assert.Throws<ArgumentNullException>(() => new DemTerrainOverlayRegion(null!, CreateBounds()));
    }

    [Fact]
    public void DemTerrainOverlayRegionRejectsNullMeshCodeInWithExpression()
    {
        DemTerrainOverlayRegion region = new(ThirdRegionalMeshCode.Parse("53394525"), CreateBounds());

        Assert.Throws<ArgumentNullException>(() => region with { MeshCode = null! });
    }

    [Fact]
    public void DemTerrainRasterCacheKeyRejectsNullMeshCodeAtConstruction()
    {
        DemTerrainRasterSourceScope scope = new("raster.tif");

        Assert.Throws<ArgumentNullException>(() =>
            new DemTerrainRasterCacheKey("dataset", scope, null!, CreateBounds()));
    }

    [Fact]
    public void PreparedTerrainOverlayTextureReferenceRejectsNullMeshCodeAtConstruction()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PreparedTerrainOverlayTextureReference(null!, CreateOverlay(), CreateGeneratedTexture()));
    }

    [Fact]
    public void PreparedTerrainOverlayTextureReferenceRejectsNullMeshCodeInWithExpression()
    {
        PreparedTerrainOverlayTextureReference reference = new(
            ThirdRegionalMeshCode.Parse("53394525"),
            CreateOverlay(),
            CreateGeneratedTexture());

        Assert.Throws<ArgumentNullException>(() => reference with { MeshCode = null! });
    }

    private static TerrainTextureOverlay CreateOverlay()
    {
        return new TerrainTextureOverlay(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            GeographicBounds: CreateBounds(),
            MaxTextureSize: 512,
            Sources: [CreateTileSource()]);
    }

    private static TerrainTextureTileSource CreateTileSource() =>
        new("https://tiles.example/{z}/{x}/{y}.png", 17);

    private static GeneratedTerrainTexture CreateGeneratedTexture() =>
        new(
            TextureImportSourceFactory.CreateInMemory(
                1,
                1,
                "sRGB",
                [1, 2, 3, 4],
                "terrain:texture",
                TexturePayloadFormat.RawRgba32),
            new ResoniteFloat2(1.0, 1.0),
            new ResoniteFloat2(0.0, 0.0),
            CreateTileSource());

    private static GeographicRectangle CreateBounds() =>
        new(35.0, 35.01, 139.0, 139.01);
}
