using System;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class CityGmlSurfaceMaterialResolverTests
{
    [Fact]
    public void CreateMaterialBindingReportsMissingRequestedMeshCodeWhenOverlayDoesNotMatchActualMeshCode()
    {
        ResolvedSurfaceMaterial representativeSurface = new(
            CreateSurface(),
            new ResolvedMaterial(
                MaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind.Bundled,
                MaterialProjection.Uv,
                Family: "terrain",
                TextureScale: null,
                MaterialReuseScope.Shared,
                TerrainOverlay: CreateOverlay("53394525")),
            DepthOffset: null);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => CityGmlSurfaceMaterialResolver.CreateMaterialBinding(
                "53394600",
                representativeSurface,
                materialIndex: 0));

        Assert.Contains("phase='material-binding'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("actual_mesh_code='53394600'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("requested_mesh_code=", exception.Message, StringComparison.Ordinal);
    }

    private static LocalCityGmlObjectProjection.ParsedSurface CreateSurface()
    {
        return new LocalCityGmlObjectProjection.ParsedSurface(
            PolygonId: "surface",
            Semantic: LocalCityGmlObjectProjection.ParsedSurfaceSemantic.Roof,
            ExteriorRing: new LocalCityGmlObjectProjection.ParsedRing(
                "surface-ring",
                [
                    new LocalCityGmlObjectProjection.GeodeticPoint(35.0, 139.0, 10.0),
                    new LocalCityGmlObjectProjection.GeodeticPoint(35.0, 139.1, 10.0),
                    new LocalCityGmlObjectProjection.GeodeticPoint(35.1, 139.1, 10.0),
                ],
                UVs: null),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null);
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
