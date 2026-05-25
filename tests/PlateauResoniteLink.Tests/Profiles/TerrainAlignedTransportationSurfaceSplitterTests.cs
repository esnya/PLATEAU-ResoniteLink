using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Application.Importing;

using ProjectionGeodeticPoint = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.GeodeticPoint;
using ProjectionParsedRing = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.ParsedRing;
using ProjectionParsedSurface = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.ParsedSurface;
using ProjectionParsedSurfaceSemantic = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.ParsedSurfaceSemantic;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class TerrainAlignedTransportationSurfaceSplitterTests
{
    [Fact]
    public void SplitDividesLongQuadAndInterpolatesUvs()
    {
        ProjectionParsedSurface surface = CreateRoadSurface(
            "road",
            [
                new Float2(0.0, 0.0),
                new Float2(1.0, 0.0),
                new Float2(1.0, 1.0),
                new Float2(0.0, 1.0),
            ]);
        Float3[] positions =
        [
            new(0.0, 0.0, 0.0),
            new(12.0, 0.0, 0.0),
            new(12.0, 0.0, 4.0),
            new(0.0, 0.0, 4.0),
        ];
        EdgePairSelection edgePair = CreateEdgePair(surface, positions, length: 12.0, width: 4.0);

        List<ProjectionParsedSurface> strips = TerrainAlignedTransportationSurfaceSplitter.Split(surface, positions, edgePair);

        Assert.Equal(4, strips.Count);
        Assert.All(strips, strip =>
        {
            Assert.StartsWith("road_terrain_", strip.PolygonId, StringComparison.Ordinal);
            Assert.Empty(strip.InteriorRings);
            Assert.Equal(surface.BaseColor, strip.BaseColor);
            Assert.Equal(surface.TexturePayload, strip.TexturePayload);
            Assert.NotNull(strip.ExteriorRing.UVs);
            Assert.Equal(strip.ExteriorRing.Vertices.Length, strip.ExteriorRing.UVs!.Count);
        });
        Assert.Contains(strips[0].ExteriorRing.UVs!, uv => uv.X > 0.0 && uv.X < 1.0);
    }

    [Fact]
    public void SplitKeepsShortQuadUnchanged()
    {
        ProjectionParsedSurface surface = CreateRoadSurface("short-road", uvs: null);
        Float3[] positions =
        [
            new(0.0, 0.0, 0.0),
            new(2.0, 0.0, 0.0),
            new(2.0, 0.0, 4.0),
            new(0.0, 0.0, 4.0),
        ];
        EdgePairSelection edgePair = CreateEdgePair(surface, positions, length: 2.0, width: 4.0);

        List<ProjectionParsedSurface> strips = TerrainAlignedTransportationSurfaceSplitter.Split(surface, positions, edgePair);

        ProjectionParsedSurface unchanged = Assert.Single(strips);
        Assert.Same(surface, unchanged);
    }

    private static ProjectionParsedSurface CreateRoadSurface(string polygonId, Float2[]? uvs)
    {
        ProjectionGeodeticPoint[] vertices =
        [
            new(35.0, 139.0, 0.0),
            new(35.0, 139.0012, 0.0),
            new(35.0004, 139.0012, 0.0),
            new(35.0004, 139.0, 0.0),
        ];

        return new ProjectionParsedSurface(
            polygonId,
            ProjectionParsedSurfaceSemantic.Ground,
            new ProjectionParsedRing($"{polygonId}-ring", vertices, uvs),
            InteriorRings: [],
            new ColorRgba(0.2, 0.2, 0.2, 1.0),
            TexturePayload: null);
    }

    private static EdgePairSelection CreateEdgePair(
        ProjectionParsedSurface surface,
        Float3[] positions,
        double length,
        double width)
    {
        ProjectionGeodeticPoint[] vertices = surface.ExteriorRing.Vertices;
        Float2[]? uvs = surface.ExteriorRing.UVs?.ToArray();
        return new EdgePairSelection(
            [vertices[0], vertices[1]],
            [vertices[3], vertices[2]],
            [positions[0], positions[1]],
            [positions[3], positions[2]],
            uvs is null ? null : [uvs[0], uvs[1]],
            uvs is null ? null : [uvs[3], uvs[2]],
            Length: length,
            Width: width,
            Side0EdgeLength: length,
            Side1EdgeLength: length);
    }
}
