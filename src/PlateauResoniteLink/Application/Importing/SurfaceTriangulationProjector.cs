using System;
using System.Collections.Generic;

using GeographicLib;


namespace PlateauResoniteLink.Application.Importing;

internal readonly record struct SurfaceTriangulationRequest(
    string PackageName,
    ParsedSurface Surface,
    ResolvedMaterial Material,
    GeodeticPoint CityObjectOrigin,
    LocalCartesian? CityObjectCartesian,
    FacadeUvProjectionContext? FacadeUvProjectionContext,
    DemTerrainTextureUvProjection? DemUvProjection);

internal static class SurfaceTriangulationProjector
{
    public static void Append(
        SurfaceTriangulationRequest request,
        List<MeshVertex> vertices,
        List<int> indices)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PackageName);
        ArgumentNullException.ThrowIfNull(request.Surface);
        ArgumentNullException.ThrowIfNull(request.Material);
        ArgumentNullException.ThrowIfNull(request.CityObjectOrigin);
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);

        bool useVertexColors = request.Material.MaterialType == MaterialType.VertexColor;
        DemTerrainTextureUvProjection? generatedDemUvProjection =
            request.Material.TerrainOverlay is not null ? request.DemUvProjection : null;
        bool useGeneratedDemUv = generatedDemUvProjection is not null;
        SurfaceGeneratedUvProjection? generatedSurfaceUvProjection = !useGeneratedDemUv
            && request.Surface.TexturePayload is null
            && request.Material.Projection == MaterialProjection.Uv
                ? SurfaceGeneratedUvProjection.TryCreate(
                    request.Surface,
                    request.PackageName,
                    request.CityObjectOrigin,
                    request.CityObjectCartesian,
                    request.FacadeUvProjectionContext)
                : null;
        List<TessellatedRing> tessellatedRings = SurfaceTessellatedRingFactory.Create(
            request.Surface,
            request.CityObjectOrigin,
            request.CityObjectCartesian,
            generatedDemUvProjection,
            generatedSurfaceUvProjection,
            useVertexColors ? request.Surface.BaseColor : null);
        if (tessellatedRings.Count == 0)
        {
            return;
        }

        SurfacePolygonTessellation? tessellation = SurfacePolygonTessellator.Tessellate(tessellatedRings);
        if (tessellation is null)
        {
            return;
        }

        SurfaceMeshTriangleAppender.Append(request.PackageName, tessellation, vertices, indices);
    }
}
