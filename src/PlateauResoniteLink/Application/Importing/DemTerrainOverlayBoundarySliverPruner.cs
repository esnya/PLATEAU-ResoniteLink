using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class DemTerrainOverlayBoundarySliverPruner
{
    private const double BoundarySliverMaxThicknessMeters = 0.10;
    private const double BoundarySliverMaxAreaRatio = 0.01;
    private const double BoundarySliverMaxAreaSquareMeters = 4.0;

    public static bool TryPruneGroups(
        IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> surfaces,
        out IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> prunedSurfaces)
    {
        prunedSurfaces = [];
        if (surfaces.Count <= 1)
        {
            return false;
        }

        GroupMetrics[] groups = surfaces
            .GroupBy(static surface => surface.Overlay)
            .Select(static group =>
            {
                (ParsedSurface Surface, TerrainTextureOverlay Overlay)[] groupSurfaces = group.ToArray();
                SurfaceMetrics[] metrics = groupSurfaces
                    .Select(static entry => ComputeSurfaceMetrics(entry.Surface))
                    .ToArray();
                return new GroupMetrics(
                    group.Key,
                    groupSurfaces,
                    metrics.Sum(static metric => metric.AreaSquareMeters),
                    metrics);
            })
            .ToArray();
        if (groups.Length <= 1)
        {
            return false;
        }

        double totalArea = groups.Sum(static group => group.AreaSquareMeters);
        if (totalArea <= 1e-9)
        {
            return false;
        }

        int dominantIndex = 0;
        for (int index = 1; index < groups.Length; index++)
        {
            if (groups[index].AreaSquareMeters > groups[dominantIndex].AreaSquareMeters)
            {
                dominantIndex = index;
            }
        }

        List<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> keptSurfaces = [];
        bool prunedBoundarySliver = false;
        for (int index = 0; index < groups.Length; index++)
        {
            GroupMetrics group = groups[index];
            if (index != dominantIndex)
            {
                double areaRatio = group.AreaSquareMeters / totalArea;
                bool isBoundarySliverGroup = group.SurfaceMetrics.Count > 0
                    && group.SurfaceMetrics.All(metric => IsBoundarySliver(metric, areaRatio));
                if (isBoundarySliverGroup)
                {
                    prunedBoundarySliver = true;
                    continue;
                }
            }

            keptSurfaces.AddRange(group.Surfaces);
        }

        if (!prunedBoundarySliver || keptSurfaces.Count == surfaces.Count || keptSurfaces.Count == 0)
        {
            return false;
        }

        prunedSurfaces = keptSurfaces
            .OrderBy(static entry => entry.Overlay.PackageName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Overlay.GeographicBounds.MinLatitude)
            .ThenBy(static entry => entry.Overlay.GeographicBounds.MinLongitude)
            .ThenBy(static entry => entry.Surface.PolygonId, StringComparer.Ordinal)
            .ToArray();
        return true;
    }

    private static SurfaceMetrics ComputeSurfaceMetrics(ParsedSurface surface)
    {
        GeodeticPoint[] vertices = surface.ExteriorRing.Vertices;
        if (vertices.Length < 3)
        {
            return new SurfaceMetrics(0.0, 0.0);
        }

        double referenceLatitude = vertices.Average(static point => point.Latitude) * (Math.PI / 180.0);
        double referenceLongitude = vertices.Average(static point => point.Longitude);
        double metersPerLatitudeDegree = 111_320.0;
        double metersPerLongitudeDegree = metersPerLatitudeDegree * Math.Cos(referenceLatitude);

        ProjectedPoint[] projected = vertices
            .Select(point => new ProjectedPoint(
                (point.Longitude - referenceLongitude) * metersPerLongitudeDegree,
                (point.Latitude - vertices[0].Latitude) * metersPerLatitudeDegree))
            .ToArray();

        double signedArea = 0.0;
        double maxDistance = 0.0;
        for (int index = 0; index < projected.Length; index++)
        {
            ProjectedPoint current = projected[index];
            ProjectedPoint next = projected[(index + 1) % projected.Length];
            signedArea += (current.X * next.Y) - (next.X * current.Y);
            maxDistance = Math.Max(maxDistance, Distance(current, next));
        }

        double areaSquareMeters = Math.Abs(signedArea) * 0.5;
        double estimatedThicknessMeters = maxDistance <= 1e-9
            ? 0.0
            : (2.0 * areaSquareMeters) / maxDistance;
        return new SurfaceMetrics(areaSquareMeters, estimatedThicknessMeters);
    }

    private static bool IsBoundarySliver(SurfaceMetrics metrics, double areaRatio)
    {
        return metrics.EstimatedThicknessMeters <= BoundarySliverMaxThicknessMeters
            && (areaRatio <= BoundarySliverMaxAreaRatio
                || metrics.AreaSquareMeters <= BoundarySliverMaxAreaSquareMeters);
    }

    private static double Distance(ProjectedPoint left, ProjectedPoint right)
    {
        double deltaX = right.X - left.X;
        double deltaY = right.Y - left.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    private readonly record struct ProjectedPoint(double X, double Y);

    private readonly record struct SurfaceMetrics(double AreaSquareMeters, double EstimatedThicknessMeters);

    private readonly record struct GroupMetrics(
        TerrainTextureOverlay Overlay,
        IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> Surfaces,
        double AreaSquareMeters,
        IReadOnlyList<SurfaceMetrics> SurfaceMetrics);
}
