using System;
using System.Collections.Generic;
using System.Linq;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class TerrainSurfaceConformer
{
    public static ParsedCityObject Conform(ParsedCityObject cityObject, TerrainHeightSampler? terrainHeightSampler)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        if (terrainHeightSampler is null || !ShouldTerrainAlignCityObject(cityObject.PackageName, cityObject.LodLevel))
        {
            return cityObject;
        }

        ParsedCityObject subdividedCityObject = ShouldSubdivideTerrainAlignedCityObject(cityObject.PackageName, cityObject.LodLevel)
            ? cityObject with { Surfaces = TerrainAlignedTransportationSurfaceSubdivider.Subdivide(cityObject) }
            : cityObject;

        bool terrainAligned = false;
        GeodeticPoint cityObjectOrigin = CityObjectGeometryMetrics.GetCenterOrigin(subdividedCityObject);
        LocalCartesian? cityObjectCartesian = subdividedCityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                subdividedCityObject.ReferenceSystem.Geocentric)
            : null;
        ParsedSurface[] conformedSurfaces = PlateauPackageCatalog.IsRoadPackage(subdividedCityObject.PackageName)
            ? ConformRoadSurfacesWithFallback(subdividedCityObject.Surfaces, terrainHeightSampler, ref terrainAligned)
            : ConformSurfaces(
                subdividedCityObject.PackageName,
                subdividedCityObject.Surfaces,
                terrainHeightSampler,
                cityObjectOrigin,
                cityObjectCartesian,
                ref terrainAligned);

        return terrainAligned
            ? subdividedCityObject with { Surfaces = conformedSurfaces, TerrainAligned = true }
            : subdividedCityObject;
    }

    public static bool ShouldTerrainAlignCityObject(string packageName, int? lodLevel)
    {
        packageName = packageName.ToLowerInvariant();
        if (PlateauPackageCatalog.IsRoadPackage(packageName))
        {
            return !lodLevel.HasValue || lodLevel.Value < 3;
        }

        return packageName switch
        {
            "fld" or "ifld" or "lsld" or "luse" or "rfld" or "tnm" or "urf" or "wtr" or "wwy" => true,
            _ => false,
        };
    }

    private static bool ShouldSubdivideTerrainAlignedCityObject(string packageName, int? lodLevel)
    {
        return PlateauPackageCatalog.IsRoadPackage(packageName)
            && (!lodLevel.HasValue || lodLevel.Value < 3);
    }

    private static ParsedSurface[] ConformSurfaces(
        string packageName,
        ParsedSurface[] surfaces,
        TerrainHeightSampler terrainHeightSampler,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        ref bool terrainAligned)
    {
        ParsedSurface[] conformedSurfaces = new ParsedSurface[surfaces.Length];
        for (int index = 0; index < surfaces.Length; index++)
        {
            ParsedSurface surface = surfaces[index];
            conformedSurfaces[index] = ShouldConformSurface(packageName, surface, cityObjectOrigin, cityObjectCartesian)
                ? ConformSurface(surface, terrainHeightSampler, ref terrainAligned)
                : surface;
        }

        return conformedSurfaces;
    }

    private static ParsedSurface[] ConformRoadSurfacesWithFallback(
        ParsedSurface[] surfaces,
        TerrainHeightSampler terrainHeightSampler,
        ref bool terrainAligned)
    {
        List<TerrainSampleAnchor> anchors = [];
        foreach (ParsedSurface surface in surfaces)
        {
            foreach (GeodeticPoint point in surface.Vertices)
            {
                if (terrainHeightSampler.TrySampleHeight(point.Latitude, point.Longitude, out double altitude, allowNearestPointFallback: false))
                {
                    anchors.Add(new TerrainSampleAnchor(point.Latitude, point.Longitude, altitude));
                }
            }
        }

        if (anchors.Count == 0)
        {
            return [.. surfaces];
        }

        ParsedSurface[] conformedSurfaces = new ParsedSurface[surfaces.Length];
        for (int index = 0; index < surfaces.Length; index++)
        {
            conformedSurfaces[index] = ConformRoadSurfaceWithFallback(
                surfaces[index],
                terrainHeightSampler,
                anchors,
                ref terrainAligned);
        }

        return conformedSurfaces;
    }

    private static ParsedSurface ConformRoadSurfaceWithFallback(
        ParsedSurface surface,
        TerrainHeightSampler terrainHeightSampler,
        IReadOnlyList<TerrainSampleAnchor> anchors,
        ref bool terrainAligned)
    {
        ParsedRing exteriorRing = ConformRoadRingWithFallback(surface.ExteriorRing, terrainHeightSampler, anchors, ref terrainAligned);
        ParsedRing[] interiorRings = new ParsedRing[surface.InteriorRings.Length];
        for (int index = 0; index < surface.InteriorRings.Length; index++)
        {
            interiorRings[index] = ConformRoadRingWithFallback(surface.InteriorRings[index], terrainHeightSampler, anchors, ref terrainAligned);
        }

        return surface with { ExteriorRing = exteriorRing, InteriorRings = interiorRings };
    }

    private static ParsedRing ConformRoadRingWithFallback(
        ParsedRing ring,
        TerrainHeightSampler terrainHeightSampler,
        IReadOnlyList<TerrainSampleAnchor> anchors,
        ref bool terrainAligned)
    {
        GeodeticPoint[] vertices = new GeodeticPoint[ring.Vertices.Length];
        for (int index = 0; index < ring.Vertices.Length; index++)
        {
            GeodeticPoint point = ring.Vertices[index];
            double altitude = terrainHeightSampler.TrySampleHeight(point.Latitude, point.Longitude, out double sampledAltitude, allowNearestPointFallback: false)
                ? sampledAltitude
                : FindNearestAnchorAltitude(point, anchors);
            if (Math.Abs(point.Altitude - altitude) > 1e-6)
            {
                terrainAligned = true;
            }

            vertices[index] = new GeodeticPoint(point.Latitude, point.Longitude, altitude);
        }

        return ring with { Vertices = vertices };
    }

    private static double FindNearestAnchorAltitude(GeodeticPoint point, IReadOnlyList<TerrainSampleAnchor> anchors)
    {
        double nearestDistanceSquared = double.MaxValue;
        double altitude = point.Altitude;
        foreach (TerrainSampleAnchor anchor in anchors)
        {
            double deltaLatitude = point.Latitude - anchor.Latitude;
            double deltaLongitude = point.Longitude - anchor.Longitude;
            double distanceSquared = (deltaLatitude * deltaLatitude) + (deltaLongitude * deltaLongitude);
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                altitude = anchor.Altitude;
            }
        }

        return altitude;
    }

    private static ParsedSurface ConformSurface(
        ParsedSurface surface,
        TerrainHeightSampler terrainHeightSampler,
        ref bool terrainAligned)
    {
        ParsedRing exteriorRing = ConformRing(surface.ExteriorRing, terrainHeightSampler, ref terrainAligned);
        ParsedRing[] interiorRings = new ParsedRing[surface.InteriorRings.Length];
        for (int index = 0; index < surface.InteriorRings.Length; index++)
        {
            interiorRings[index] = ConformRing(surface.InteriorRings[index], terrainHeightSampler, ref terrainAligned);
        }

        return surface with { ExteriorRing = exteriorRing, InteriorRings = interiorRings };
    }

    private static ParsedRing ConformRing(
        ParsedRing ring,
        TerrainHeightSampler terrainHeightSampler,
        ref bool terrainAligned)
    {
        GeodeticPoint[] vertices = new GeodeticPoint[ring.Vertices.Length];
        bool[] sampled = new bool[ring.Vertices.Length];
        int sampledCount = 0;

        for (int index = 0; index < ring.Vertices.Length; index++)
        {
            GeodeticPoint point = ring.Vertices[index];
            if (!terrainHeightSampler.TrySampleHeight(point.Latitude, point.Longitude, out double altitude, allowNearestPointFallback: false))
            {
                vertices[index] = point;
                continue;
            }

            sampled[index] = true;
            sampledCount++;
            if (Math.Abs(point.Altitude - altitude) > 1e-6)
            {
                terrainAligned = true;
            }

            vertices[index] = new GeodeticPoint(point.Latitude, point.Longitude, altitude);
        }

        if (sampledCount > 0 && sampledCount < vertices.Length)
        {
            InterpolateUnsampledVertices(vertices, sampled, ref terrainAligned);
        }

        return ring with { Vertices = vertices };
    }

    private static void InterpolateUnsampledVertices(GeodeticPoint[] vertices, bool[] sampled, ref bool terrainAligned)
    {
        for (int index = 0; index < vertices.Length; index++)
        {
            if (sampled[index])
            {
                continue;
            }

            int previousSampledIndex = FindPreviousSampledIndex(sampled, index);
            int nextSampledIndex = FindNextSampledIndex(sampled, index);
            double altitude = ResolveInterpolatedAltitude(vertices, index, previousSampledIndex, nextSampledIndex);
            if (Math.Abs(vertices[index].Altitude - altitude) > 1e-6)
            {
                terrainAligned = true;
            }

            vertices[index] = new GeodeticPoint(vertices[index].Latitude, vertices[index].Longitude, altitude);
            sampled[index] = true;
        }
    }

    private static double ResolveInterpolatedAltitude(
        GeodeticPoint[] vertices,
        int index,
        int previousSampledIndex,
        int nextSampledIndex)
    {
        if (previousSampledIndex >= 0 && nextSampledIndex >= 0 && previousSampledIndex != nextSampledIndex)
        {
            int previousToIndexSteps = (index - previousSampledIndex + vertices.Length) % vertices.Length;
            int previousToNextSteps = (nextSampledIndex - previousSampledIndex + vertices.Length) % vertices.Length;
            if (previousToNextSteps > 0)
            {
                double ratio = (double)previousToIndexSteps / previousToNextSteps;
                return vertices[previousSampledIndex].Altitude
                    + ((vertices[nextSampledIndex].Altitude - vertices[previousSampledIndex].Altitude) * ratio);
            }
        }

        int fallbackIndex = previousSampledIndex >= 0
            ? previousSampledIndex
            : nextSampledIndex;
        return vertices[fallbackIndex].Altitude;
    }

    private static int FindPreviousSampledIndex(bool[] sampled, int index)
    {
        for (int offset = 1; offset < sampled.Length; offset++)
        {
            int candidate = (index - offset + sampled.Length) % sampled.Length;
            if (sampled[candidate])
            {
                return candidate;
            }
        }

        return -1;
    }

    private static int FindNextSampledIndex(bool[] sampled, int index)
    {
        for (int offset = 1; offset < sampled.Length; offset++)
        {
            int candidate = (index + offset) % sampled.Length;
            if (sampled[candidate])
            {
                return candidate;
            }
        }

        return -1;
    }

    private static bool ShouldConformSurface(
        string packageName,
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (!PlateauPackageCatalog.IsRoadPackage(packageName))
        {
            return true;
        }

        Float3[] positions = surface.ExteriorRing.Vertices
            .Select(point => SceneAxisMapper.CreatePosition(
                point.Latitude,
                point.Longitude,
                point.Altitude,
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                cityObjectCartesian))
            .ToArray();
        return IsNearHorizontalSurface(positions);
    }

    private static bool IsNearHorizontalSurface(IEnumerable<Float3> positions)
    {
        Float3? normal = SurfaceGeometryMath.ComputeNewellNormal(positions);
        return normal is not null && Math.Abs(normal.Y) >= 0.7;
    }

    private readonly record struct TerrainSampleAnchor(double Latitude, double Longitude, double Altitude);
}
