using PlateauResoniteLink.Application.Importing.CityGml;
using PlateauResoniteLink.Application.Importing.Contracts;
using PlateauResoniteLink.Application.Importing.Plateau;
using PlateauResoniteLink.Application.Importing.Source;

using System;
using System.Collections.Generic;
using System.Linq;

using GeographicLib;


namespace PlateauResoniteLink.Tests.Profiles;

public sealed class CityGmlSurfaceProjectionPolicyTests
{
    [Fact]
    public void GetCulledSurfacesBeforeProjectionCullsBuildingBottomBandOnlyWhenHigherGeometryExists()
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

        HashSet<ParsedSurface> buildingCull = CityGmlSurfaceProjectionPolicy.GetCulledSurfacesBeforeProjection(
            CreateDraft("bldg", [wall, bottom, roof]),
            origin,
            cartesian);
        HashSet<ParsedSurface> roadCull = CityGmlSurfaceProjectionPolicy.GetCulledSurfacesBeforeProjection(
            CreateDraft("tran", [bottom]),
            origin,
            cartesian);

        Assert.Contains(bottom, buildingCull);
        Assert.DoesNotContain(roof, buildingCull);
        Assert.Empty(roadCull);
    }

    [Fact]
    public void TryCreateFacadeUvProjectionContextUsesPreRoofGenerationWallRangeWhenAvailable()
    {
        GeodeticPoint origin = new(35.0, 139.0, 0.0);
        LocalCartesian cartesian = CreateCartesian(origin);
        ParsedSurface wall = CreateSurface(
            "wall",
            ParsedSurfaceSemantic.Wall,
            CreateVerticalQuadVertices(origin, widthMeters: 8.0, heightMeters: 6.0));
        ParsedSurface generatedRoof = CreateSurface(
            "bldg_roof_gable-top",
            ParsedSurfaceSemantic.Roof,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 9.0, sizeMeters: 8.0, reverseWinding: true));

        FacadeUvProjectionContext? context = CityGmlSurfaceProjectionPolicy.TryCreateFacadeUvProjectionContext(
            CreateDraft("bldg", [wall, generatedRoof], facadeUvReferenceSurfaces: [wall]),
            origin,
            cartesian);

        Assert.NotNull(context);
        Assert.InRange(context.Value.MinimumY, -1e-5, 1e-5);
        Assert.InRange(context.Value.MaximumY, 6.0 - 1e-5, 6.0 + 1e-5);
    }

    [Fact]
    public void TryCreateFacadeUvProjectionContextDoesNotCullRoofSlabAtObjectMinimum()
    {
        GeodeticPoint origin = new(35.0, 139.0, 0.0);
        LocalCartesian cartesian = CreateCartesian(origin);
        ParsedSurface wall = CreateSurface(
            "wall",
            ParsedSurfaceSemantic.Wall,
            CreateVerticalQuadVertices(origin, widthMeters: 8.0, heightMeters: 6.0));
        ParsedSurface generatedNoWallBottom = CreateSurface(
            "bldg_roof-slab-bottom",
            ParsedSurfaceSemantic.Roof,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 8.7, sizeMeters: 8.0, reverseWinding: true));

        HashSet<ParsedSurface> culledSurfaces = CityGmlSurfaceProjectionPolicy.GetCulledSurfacesBeforeProjection(
            CreateDraft("bldg", [new ConstructionFace(wall, ConstructionFaceRole.Wall), new ConstructionFace(generatedNoWallBottom, ConstructionFaceRole.RoofSlab)]),
            origin,
            cartesian);
        FacadeUvProjectionContext? context = CityGmlSurfaceProjectionPolicy.TryCreateFacadeUvProjectionContext(
            CreateDraft(
                "bldg",
                [new ConstructionFace(wall, ConstructionFaceRole.Wall), new ConstructionFace(generatedNoWallBottom, ConstructionFaceRole.RoofSlab)],
                facadeUvReferenceSurfaces: [wall]),
            origin,
            cartesian);

        Assert.Empty(culledSurfaces);
        Assert.NotNull(context);
        Assert.InRange(context.Value.MinimumY, -1e-5, 1e-5);
        Assert.InRange(context.Value.MaximumY, 6.0 - 1e-5, 6.0 + 1e-5);
    }

    [Fact]
    public void TryCreateFacadeUvProjectionContextSkipsEmptySurfacesBeforeResolvingHeightRange()
    {
        GeodeticPoint origin = new(35.0, 139.0, 0.0);
        LocalCartesian cartesian = CreateCartesian(origin);
        ParsedSurface empty = CreateSurface("empty", ParsedSurfaceSemantic.Wall, []);
        ParsedSurface wall = CreateSurface(
            "wall",
            ParsedSurfaceSemantic.Wall,
            CreateVerticalQuadVertices(origin, widthMeters: 8.0, heightMeters: 6.0));

        FacadeUvProjectionContext? context = CityGmlSurfaceProjectionPolicy.TryCreateFacadeUvProjectionContext(
            CreateDraft("bldg", [empty, wall]),
            origin,
            cartesian);

        Assert.NotNull(context);
        Assert.InRange(context.Value.MinimumY, -1e-5, 1e-5);
        Assert.InRange(context.Value.MaximumY, 6.0 - 1e-5, 6.0 + 1e-5);
    }

    [Fact]
    public void GetCulledSurfacesBeforeProjectionKeepsRoofSlabBottomAtObjectMinimum()
    {
        GeodeticPoint origin = new(35.0, 139.0, 0.0);
        LocalCartesian cartesian = CreateCartesian(origin);
        ParsedSurface generatedNoWallBottom = CreateSurface(
            "roof_roof-slab-bottom",
            ParsedSurfaceSemantic.Roof,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 9.7, sizeMeters: 8.0));
        ParsedSurface generatedNoWallSide = CreateSurface(
            "roof_roof-slab-side-0",
            ParsedSurfaceSemantic.Roof,
            CreateVerticalQuadVertices(origin with { Altitude = 9.7 }, widthMeters: 8.0, heightMeters: 0.3));
        ParsedSurface roof = CreateSurface(
            "roof",
            ParsedSurfaceSemantic.Roof,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 10.0, sizeMeters: 8.0, reverseWinding: true));

        HashSet<ParsedSurface> culledSurfaces = CityGmlSurfaceProjectionPolicy.GetCulledSurfacesBeforeProjection(
            CreateDraft(
                "bldg",
                [
                    new ConstructionFace(roof, ConstructionFaceRole.Roof),
                    new ConstructionFace(generatedNoWallBottom, ConstructionFaceRole.RoofSlab),
                    new ConstructionFace(generatedNoWallSide, ConstructionFaceRole.RoofSlab),
                ]),
            origin,
            cartesian);

        Assert.Empty(culledSurfaces);
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
        _ = polygonId;
        return new ParsedSurface(
            semantic,
            new ParsedRing(vertices.ToArray(), UVs: null),
            InteriorRings: [],
            BaseColor: new ColorRgba(0.5, 0.5, 0.5, 1.0),
            TexturePayload: null);
    }

    private static ConstructionCityObjectDraft CreateDraft(
        string packageName,
        ParsedSurface[] surfaces,
        ParsedSurface[]? facadeUvReferenceSurfaces = null)
    {
        return CreateDraft(
            packageName,
            surfaces.Select(static surface => new ConstructionFace(surface, ConstructionCityObjectDraft.ResolveRole(surface))).ToArray(),
            facadeUvReferenceSurfaces);
    }

    private static ConstructionCityObjectDraft CreateDraft(
        string packageName,
        ConstructionFace[] faces,
        ParsedSurface[]? facadeUvReferenceSurfaces = null)
    {
        ParsedCityObject cityObject = new(
            SlotKey: $"{packageName}-object",
            DisplayName: packageName,
            PackageName: packageName,
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Surfaces: faces.Select(static face => face.Surface).ToArray(),
            ReferenceSystem: CoordinateReferenceSystem.Parse("EPSG:4326"),
            SourceFileRelativePath: $"udx/{packageName}/53394525/sample.gml",
            SharedAcrossMeshCodes: false,
            BuildingAttributes: BuildingAttributeContext.Empty);
        ConstructionFace[]? referenceFaces = facadeUvReferenceSurfaces is null
            ? null
            : facadeUvReferenceSurfaces
                .Select(static surface => new ConstructionFace(surface, ConstructionCityObjectDraft.ResolveRole(surface)))
                .ToArray();
        return new ConstructionCityObjectDraft(cityObject, faces, referenceFaces);
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
