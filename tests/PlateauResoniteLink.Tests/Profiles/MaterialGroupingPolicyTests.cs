using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class MaterialGroupingPolicyTests
{
    [Fact]
    public void CreateKeyGroupsMatchingNonOverlayMaterials()
    {
        ResolvedMaterial material = new(
            MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind.Dataset,
            MaterialProjection.Uv,
            Family: "facade",
            TextureScale: new Float2(2.0, 3.0),
            MaterialReuseScope.Shared,
            BundledVariantIndex: 7,
            TextureOffset: new Float2(0.25, 0.5));
        ColorRgba color = new(0.1, 0.2, 0.3, 1.0);

        MaterialGroupingKey first = MaterialGroupingPolicy.CreateKey(
            "53394525",
            material,
            depthOffset: new MaterialDepthOffset(1.0, 2.0),
            textureScale: material.TextureScale,
            color,
            textureOffset: material.TextureOffset);
        MaterialGroupingKey second = MaterialGroupingPolicy.CreateKey(
            "53394525",
            material,
            depthOffset: new MaterialDepthOffset(1.0, 2.0),
            textureScale: material.TextureScale,
            color,
            textureOffset: material.TextureOffset);

        Assert.Equal(first, second);
    }

    [Fact]
    public void CreateKeySeparatesNonOverlayMaterialsWhenGroupingInputsDiffer()
    {
        ResolvedMaterial material = new(
            MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind.Dataset,
            MaterialProjection.Uv,
            Family: "facade",
            TextureScale: null,
            MaterialReuseScope.Shared);

        MaterialGroupingKey first = MaterialGroupingPolicy.CreateKey(
            "53394525",
            material,
            depthOffset: null,
            textureScale: null,
            color: new ColorRgba(0.1, 0.2, 0.3, 1.0));
        MaterialGroupingKey second = MaterialGroupingPolicy.CreateKey(
            "53394525",
            material,
            depthOffset: null,
            textureScale: null,
            color: new ColorRgba(0.2, 0.2, 0.3, 1.0));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void CreateKeyGroupsTerrainOverlayMaterialsWhenTransformsAreNoOp()
    {
        TerrainTextureOverlay overlay = CreateOverlay("53394525");
        ResolvedMaterial material = new(
            MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind.Bundled,
            MaterialProjection.Uv,
            Family: "terrain",
            TextureScale: new Float2(1.0, 1.0),
            MaterialReuseScope.Shared,
            TerrainOverlay: overlay,
            TextureOffset: new Float2(0.0, 0.0));

        MaterialGroupingKey explicitNoOp = MaterialGroupingPolicy.CreateKey(
            "53394525",
            material,
            depthOffset: null,
            textureScale: material.TextureScale,
            color: new ColorRgba(0.1, 0.2, 0.3, 1.0),
            textureOffset: material.TextureOffset);
        MaterialGroupingKey implicitNoOp = MaterialGroupingPolicy.CreateKey(
            "53394525",
            material,
            depthOffset: null,
            textureScale: null,
            color: new ColorRgba(0.8, 0.7, 0.6, 1.0),
            textureOffset: null);

        Assert.Equal(explicitNoOp, implicitNoOp);
    }

    [Fact]
    public void CreateKeySeparatesTerrainOverlayMaterialsWhenTransformChanges()
    {
        TerrainTextureOverlay overlay = CreateOverlay("53394525");
        ResolvedMaterial material = new(
            MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind.Bundled,
            MaterialProjection.Uv,
            Family: "terrain",
            TextureScale: null,
            MaterialReuseScope.Shared,
            TerrainOverlay: overlay);

        MaterialGroupingKey first = MaterialGroupingPolicy.CreateKey(
            "53394525",
            material,
            depthOffset: null,
            textureScale: null,
            color: new ColorRgba(0.1, 0.2, 0.3, 1.0),
            textureOffset: null);
        MaterialGroupingKey second = MaterialGroupingPolicy.CreateKey(
            "53394525",
            material,
            depthOffset: null,
            textureScale: new Float2(2.0, 1.0),
            color: new ColorRgba(0.8, 0.7, 0.6, 1.0),
            textureOffset: null);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void CreateKeyUsesOverlayMeshCodeWithoutMatchingActualMeshCode()
    {
        TerrainTextureOverlay overlay = CreateOverlay("53394525");
        ResolvedMaterial material = new(
            MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind.Bundled,
            MaterialProjection.Uv,
            Family: null,
            TextureScale: null,
            MaterialReuseScope.Shared,
            TerrainOverlay: overlay);

        MaterialGroupingKey key = MaterialGroupingPolicy.CreateKey(
            "53394600",
            material,
            depthOffset: null,
            textureScale: null,
            color: new ColorRgba(1.0, 1.0, 1.0, 1.0));

        Assert.Null(key.TerrainOverlay);
        Assert.Equal(MaterialReuseScope.PerObject, key.ReuseScope);
    }

    [Fact]
    public void CreateKeyUsesTypedOverlayMeshCodeForRoundedOverlayBounds()
    {
        TerrainTextureOverlay overlay = CreateOverlay(
            "53391410",
            new GeographicRectangle(35.45, 35.458333, 139.4875, 139.5));
        ResolvedMaterial material = new(
            MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind.Bundled,
            MaterialProjection.Uv,
            Family: null,
            TextureScale: null,
            MaterialReuseScope.Shared,
            TerrainOverlay: overlay);

        MaterialGroupingKey key = MaterialGroupingPolicy.CreateKey(
            "533914",
            material,
            depthOffset: null,
            textureScale: null,
            color: new ColorRgba(1.0, 1.0, 1.0, 1.0));

        Assert.Null(key.TerrainOverlay);
    }

    private static TerrainTextureOverlay CreateOverlay(string meshCode, GeographicRectangle? geographicBounds = null)
    {
        Assert.True(PlateauMeshCode.TryGetBounds(
            meshCode,
            out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds));

        return new TerrainTextureOverlay(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse(meshCode),
            UrlTemplate: $"https://terrain.example/{meshCode}/{{z}}/{{x}}/{{y}}.png",
            ZoomLevel: 18,
            GeographicBounds: geographicBounds ?? new GeographicRectangle(
                bounds.SouthLatitude,
                bounds.NorthLatitude,
                bounds.WestLongitude,
                bounds.EastLongitude),
            MaxTextureSize: 2048);
    }
}
