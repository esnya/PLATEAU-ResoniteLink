using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class TerrainOverlayMeshCodeResolverTests
{
    [Fact]
    public void ResolveMeshCodeKeepsMatchingThirdMeshCode()
    {
        TerrainTextureOverlay overlay = CreateOverlay("53394525");

        ThirdRegionalMeshCode? meshCode = TerrainOverlayMeshCodeResolver.ResolveMeshCode("53394525", overlay);

        Assert.Equal("53394525", meshCode?.Value);
    }

    [Fact]
    public void ResolveMeshCodeFindsMatchingThirdMeshCodeUnderParentMesh()
    {
        TerrainTextureOverlay overlay = CreateOverlay("53394525");

        ThirdRegionalMeshCode? meshCode = TerrainOverlayMeshCodeResolver.ResolveMeshCode("533945", overlay);

        Assert.Equal("53394525", meshCode?.Value);
    }

    [Fact]
    public void ResolveForOverlayFallsBackToRequestedMeshCode()
    {
        TerrainTextureOverlay overlay = CreateOverlay("53394525");

        ThirdRegionalMeshCode? meshCode = TerrainOverlayMeshCodeResolver.ResolveForOverlay(
            actualMeshCode: "53394600",
            requestedMeshCode: "533945",
            requestedMeshCodeBounds: [],
            overlay);

        Assert.Equal("53394525", meshCode?.Value);
    }

    [Fact]
    public void ResolveForOverlayUsesRequestedOverlayBoundsWhenActualAndRequestedMeshCodesDoNotMatch()
    {
        TerrainTextureOverlay overlay = CreateOverlay("53394525");

        ThirdRegionalMeshCode? meshCode = TerrainOverlayMeshCodeResolver.ResolveForOverlay(
            actualMeshCode: "533946",
            requestedMeshCode: "533947",
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("53394525")!],
            overlay);

        Assert.Equal("53394525", meshCode?.Value);
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

    private static TerrainTextureOverlay CreateOverlay(string meshCode)
    {
        Assert.True(PlateauMeshCode.TryGetBounds(
            meshCode,
            out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds));

        return new TerrainTextureOverlay(
            PackageName: "dem",
            UrlTemplate: $"https://terrain.example/{meshCode}/{{z}}/{{x}}/{{y}}.png",
            ZoomLevel: 18,
            GeographicBounds: new GeographicRectangle(
                bounds.SouthLatitude,
                bounds.NorthLatitude,
                bounds.WestLongitude,
                bounds.EastLongitude),
            MaxTextureSize: 2048);
    }
}
