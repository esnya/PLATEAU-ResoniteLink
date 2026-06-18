using PlateauResoniteLink.Application.Importing.Plateau;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class TerrainOverlayMeshCodeResolverTests
{
    [Fact]
    public void IsRequestedOverlayUsesOverlayMeshCode()
    {
        TerrainTextureOverlay overlay = CreateOverlay("53394525");

        bool isRequested = TerrainOverlayMeshCodeResolver.IsRequestedOverlay(
            overlay,
            [MeshCodeBounds.TryParse("53394525")!]);

        Assert.True(isRequested);
    }

    [Fact]
    public void IsRequestedOverlayTreatsContainingParentMeshBoundsAsRequested()
    {
        TerrainTextureOverlay overlay = CreateOverlay("53394525");

        bool isRequested = TerrainOverlayMeshCodeResolver.IsRequestedOverlay(
            overlay,
            [MeshCodeBounds.TryParse("533945")!]);

        Assert.True(isRequested);
    }

    [Fact]
    public void BoundsOverlapRejectsBoundaryTouchOnly()
    {
        GeographicRectangle left = new(35.0, 35.1, 139.0, 139.1);
        GeographicRectangle right = new(35.1, 35.2, 139.0, 139.1);

        Assert.False(TerrainOverlayMeshCodeResolver.BoundsOverlap(left, right));
    }

    private static TerrainTextureOverlay CreateOverlay(string meshCode)
    {
        ThirdRegionalMeshCode thirdMeshCode = ThirdRegionalMeshCode.Parse(meshCode);
        JisRegionalMeshBounds bounds = thirdMeshCode.Bounds;

        return new TerrainTextureOverlay(
            PackageName: "dem",
            MeshCode: thirdMeshCode,
            UrlTemplate: $"https://terrain.example/{meshCode}/{{z}}/{{x}}/{{y}}.png",
            ZoomLevel: 18,
            GeographicBounds: bounds.ToGeographicRectangle(),
            MaxTextureSize: 2048);
    }
}

internal static class JisRegionalMeshBoundsTestExtensions
{
    public static GeographicRectangle ToGeographicRectangle(this JisRegionalMeshBounds bounds)
    {
        return new GeographicRectangle(
            bounds.SouthLatitude,
            bounds.NorthLatitude,
            bounds.WestLongitude,
            bounds.EastLongitude);
    }
}
