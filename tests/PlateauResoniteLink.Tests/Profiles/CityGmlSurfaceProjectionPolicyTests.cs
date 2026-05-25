using System;
using System.Collections.Generic;
using System.Linq;

using GeographicLib;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class CityGmlSurfaceProjectionPolicyTests
{
    [Fact]
    public void GetCulledSurfaceIdsBeforeProjectionCullsBuildingBottomBandOnlyWhenHigherGeometryExists()
    {
        GeodeticPoint origin = new(35.0, 139.0, 0.0);
        LocalCartesian cartesian = CreateCartesian(origin);
        ParsedSurface wall = CreateSurface(
            "wall",
            ParsedSurfaceSemantic.Wall,
            CreateVerticalQuadVertices(origin, widthMeters: 8.0, heightMeters: 6.0));
        ParsedSurface bottom = CreateSurface(
            "bottom",
            ParsedSurfaceSemantic.Unknown,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 0.0, sizeMeters: 8.0));
        ParsedSurface roof = CreateSurface(
            "roof",
            ParsedSurfaceSemantic.Unknown,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 6.0, sizeMeters: 8.0, reverseWinding: true));

        HashSet<string> buildingCull = CityGmlSurfaceProjectionPolicy.GetCulledSurfaceIdsBeforeProjection(
            "bldg",
            [wall, bottom, roof],
            origin,
            cartesian);
        HashSet<string> roadCull = CityGmlSurfaceProjectionPolicy.GetCulledSurfaceIdsBeforeProjection(
            "tran",
            [bottom],
            origin,
            cartesian);

        Assert.Contains("bottom", buildingCull);
        Assert.DoesNotContain("roof", buildingCull);
        Assert.Empty(roadCull);
    }

    [Fact]
    public void TryCreateFacadeUvProjectionContextIgnoresGeneratedLod1RoofSurfacesWhenWallRangeExists()
    {
        GeodeticPoint origin = new(35.0, 139.0, 0.0);
        LocalCartesian cartesian = CreateCartesian(origin);
        ParsedSurface wall = CreateSurface(
            "wall",
            ParsedSurfaceSemantic.Wall,
            CreateVerticalQuadVertices(origin, widthMeters: 8.0, heightMeters: 6.0));
        ParsedSurface generatedRoof = CreateSurface(
            "bldg_generated_gable-top",
            ParsedSurfaceSemantic.Roof,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 9.0, sizeMeters: 8.0, reverseWinding: true));

        FacadeUvProjectionContext? context = CityGmlSurfaceProjectionPolicy.TryCreateFacadeUvProjectionContext(
            "bldg",
            [wall, generatedRoof],
            origin,
            cartesian);

        Assert.NotNull(context);
        Assert.InRange(context.Value.MinimumY, -1e-5, 1e-5);
        Assert.InRange(context.Value.MaximumY, 6.0 - 1e-5, 6.0 + 1e-5);
    }

    private static LocalCartesian CreateCartesian(GeodeticPoint origin)
    {
        return new LocalCartesian(origin.Latitude, origin.Longitude, origin.Altitude, Geocentric.WGS84);
    }

    private static ParsedSurface CreateSurface(
        string polygonId,
        ParsedSurfaceSemantic semantic,
        IReadOnlyList<GeodeticPoint> vertices)
    {
        return new ParsedSurface(
            polygonId,
            semantic,
            new ParsedRing($"{polygonId}-ring", vertices.ToArray(), UVs: null),
            InteriorRings: [],
            BaseColor: new ColorRgba(0.5, 0.5, 0.5, 1.0),
            TexturePayload: null);
    }

    private static IReadOnlyList<GeodeticPoint> CreateHorizontalQuadVertices(
        GeodeticPoint origin,
        double altitudeMeters,
        double sizeMeters,
        bool reverseWinding = false)
    {
        double latitudeDelta = sizeMeters / 111320.0;
        double longitudeDelta = sizeMeters / (111320.0 * Math.Cos(origin.Latitude * (Math.PI / 180.0)));
        GeodeticPoint[] vertices =
        [
            new(origin.Latitude, origin.Longitude, altitudeMeters),
            new(origin.Latitude + latitudeDelta, origin.Longitude, altitudeMeters),
            new(origin.Latitude + latitudeDelta, origin.Longitude + longitudeDelta, altitudeMeters),
            new(origin.Latitude, origin.Longitude + longitudeDelta, altitudeMeters),
        ];
        if (reverseWinding)
        {
            Array.Reverse(vertices);
        }

        return [.. vertices, vertices[0]];
    }

    private static IReadOnlyList<GeodeticPoint> CreateVerticalQuadVertices(
        GeodeticPoint origin,
        double widthMeters,
        double heightMeters)
    {
        double longitudeDelta = widthMeters / (111320.0 * Math.Cos(origin.Latitude * (Math.PI / 180.0)));
        return
        [
            origin,
            new(origin.Latitude, origin.Longitude + longitudeDelta, origin.Altitude),
            new(origin.Latitude, origin.Longitude + longitudeDelta, origin.Altitude + heightMeters),
            new(origin.Latitude, origin.Longitude, origin.Altitude + heightMeters),
            origin,
        ];
    }
}
