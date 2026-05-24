using System.Collections.Generic;
using System.Linq;

using GeographicLib;


namespace PlateauResoniteLink.Application.Importing;

internal static class SurfaceTessellatedRingFactory
{
    public static List<TessellatedRing> Create(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        DemTerrainTextureUvProjection? generatedDemUvProjection,
        SurfaceGeneratedUvProjection? generatedSurfaceUvProjection,
        ColorRgba? vertexColor)
    {
        List<TessellatedRing> rings =
        [
            CreateRing(
                surface.ExteriorRing,
                cityObjectOrigin,
                cityObjectCartesian,
                generatedDemUvProjection,
                generatedSurfaceUvProjection,
                vertexColor),
        ];
        rings.AddRange(surface.InteriorRings.Select(ring => CreateRing(
            ring,
            cityObjectOrigin,
            cityObjectCartesian,
            generatedDemUvProjection,
            generatedSurfaceUvProjection,
            vertexColor)));
        return rings.Where(static ring => ring.Vertices.Count >= 3).ToList();
    }

    private static TessellatedRing CreateRing(
        ParsedRing ring,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        DemTerrainTextureUvProjection? generatedDemUvProjection,
        SurfaceGeneratedUvProjection? generatedSurfaceUvProjection,
        ColorRgba? vertexColor)
    {
        TessellatedVertex[] vertices = ring.Vertices
            .Select((point, index) => new TessellatedVertex(
                SceneAxisMapper.CreatePosition(
                    point.Latitude,
                    point.Longitude,
                    point.Altitude,
                    cityObjectOrigin.Latitude,
                    cityObjectOrigin.Longitude,
                    cityObjectOrigin.Altitude,
                    cityObjectCartesian),
                generatedDemUvProjection is not null
                    ? generatedDemUvProjection.Value.CreateUv(point)
                    : ring.UVs is not null && index < ring.UVs.Count
                        ? ring.UVs[index]
                        : generatedSurfaceUvProjection is not null
                        ? generatedSurfaceUvProjection.CreateUv(point, cityObjectOrigin, cityObjectCartesian)
                        : new Float2(0.0, 0.0),
                vertexColor))
            .ToArray();
        return new TessellatedRing(ring.RingId, vertices);
    }
}

internal sealed record TessellatedVertex(
    Float3 Position,
    Float2 UV,
    ColorRgba? Color);

internal sealed record TessellatedRing(
    string RingId,
    IReadOnlyList<TessellatedVertex> Vertices);
