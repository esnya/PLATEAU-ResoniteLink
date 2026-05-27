using System;
using System.Collections.Generic;
using System.Linq;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class GeneratedLod1RoofCityObjectFactory
{
    private const double BuildingBottomCullBandMeters = 0.1;
    private const double NoWallRoofThicknessMeters = 0.3;
    private static readonly string[] NoWallBuildingClassCodes = ["3003", "3004"];

    internal static ParsedCityObject Create(ParsedCityObject cityObject)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        if (!PlateauPackageCatalog.IsBuildingPackage(cityObject.PackageName)
            || !cityObject.ReferenceSystem.IsGeographic
            || cityObject.Surfaces.Any(GeneratedLod1RoofSurfaceIdentity.IsGenerated))
        {
            return cityObject;
        }

        bool isNoWallBuilding = IsNoWallBuilding(cityObject);
        if (!isNoWallBuilding && cityObject.LodLevel != 1)
        {
            return cityObject;
        }

        if (isNoWallBuilding
            && (cityObject.Surfaces.Any(static surface => surface.UsesGeneratedDemTexture)
                || !cityObject.Surfaces.Any(static surface => surface.Vertices.Any())
                || (cityObject.LodLevel >= 2 && !cityObject.Surfaces.Any(static surface => surface.Semantic == ParsedSurfaceSemantic.Roof))))
        {
            return cityObject;
        }

        GeodeticPoint cityObjectOrigin = CityObjectOriginResolver.Resolve(
            cityObject.GeodeticOriginOverride,
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices));
        LocalCartesian cityObjectCartesian = new(
            cityObjectOrigin.Latitude,
            cityObjectOrigin.Longitude,
            cityObjectOrigin.Altitude,
            cityObject.ReferenceSystem.Geocentric);
        if (isNoWallBuilding)
        {
            return TryCreateNoWallRoofSlab(cityObject, cityObjectOrigin, cityObjectCartesian, out ParsedCityObject? noWallRoofSlab)
                ? noWallRoofSlab!
                : cityObject;
        }

        if (cityObject.LodLevel != 1)
        {
            return cityObject;
        }

        if (!TryCreateFootprint(cityObject, cityObjectOrigin, cityObjectCartesian, out Lod1RoofFootprint? footprint))
        {
            return cityObject;
        }

        Lod1RoofFootprint resolvedFootprint = footprint!;
        GeneratedLod1RoofShape roofShape = Lod1RoofShapePolicy.Select(
            cityObject.SlotKey,
            resolvedFootprint.Attributes,
            resolvedFootprint.GeometryHeightMeters,
            resolvedFootprint.LengthMeters,
            resolvedFootprint.WidthMeters);
        if (roofShape == GeneratedLod1RoofShape.Flat)
        {
            return cityObject;
        }

        ParsedSurface[] generatedSurfaces = GeneratedLod1RoofSurfaceFactory.Create(resolvedFootprint, roofShape);
        if (generatedSurfaces.Length == 0)
        {
            return cityObject;
        }

        ParsedSurface[] surfaces =
        [
            .. cityObject.Surfaces.Where(surface => !string.Equals(surface.PolygonId, resolvedFootprint.TopSurface.PolygonId, StringComparison.Ordinal)),
            .. generatedSurfaces,
        ];
        return cityObject with { Surfaces = surfaces };
    }

    private static bool IsNoWallBuilding(ParsedCityObject cityObject)
    {
        return cityObject.LodLevel >= 1
            && cityObject.BuildingAttributes is not null
            && NoWallBuildingClassCodes.Any(code => BuildingAttributePredicates.HasExactCityGmlClassCode(cityObject.BuildingAttributes, code));
    }

    private static bool TryCreateNoWallRoofSlab(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian cityObjectCartesian,
        out ParsedCityObject? noWallRoofSlab)
    {
        noWallRoofSlab = null;
        if (cityObject.LodLevel == 1)
        {
            if (!TryGetNoWallTopSurfaces(
                cityObject,
                cityObjectOrigin,
                cityObjectCartesian,
                out ParsedSurface[]? topSurfaces))
            {
                return false;
            }

            ParsedSurface[] lod1RoofSurfaces = topSurfaces!
                .Select(static surface => surface with { Semantic = ParsedSurfaceSemantic.Roof })
                .ToArray();
            if (!TryCreateNoWallRoofSurfaces(lod1RoofSurfaces, includeTopSurfaces: true, out ParsedSurface[]? generatedSurfaces))
            {
                return false;
            }

            noWallRoofSlab = cityObject with { Surfaces = generatedSurfaces! };
            return true;
        }

        if (cityObject.LodLevel < 2)
        {
            return false;
        }

        ParsedSurface[] roofSurfaces = cityObject.Surfaces
            .Where(static surface => surface.Semantic == ParsedSurfaceSemantic.Roof)
            .ToArray();
        if (roofSurfaces.Length == 0
            || roofSurfaces.Any(static surface => surface.InteriorRings.Length != 0))
        {
            return false;
        }

        SurfaceProjectionInfo[] surfaceInfos = cityObject.Surfaces
            .Select(surface => CreateSurfaceProjectionInfo(surface, cityObjectOrigin, cityObjectCartesian))
            .Where(static info => info.MinimumY.HasValue && info.MaximumY.HasValue)
            .ToArray();
        if (surfaceInfos.Length == 0)
        {
            return false;
        }

        Dictionary<string, SurfaceProjectionInfo> infosByPolygonId = surfaceInfos.ToDictionary(
            static info => info.Surface.PolygonId,
            static info => info,
            StringComparer.Ordinal);
        foreach (ParsedSurface roofSurface in roofSurfaces)
        {
            if (!infosByPolygonId.TryGetValue(roofSurface.PolygonId, out SurfaceProjectionInfo roofInfo)
                || IsNoWallLod2BottomInCullBand(roofInfo, surfaceInfos))
            {
                return false;
            }
        }

        if (!TryCreateNoWallRoofSurfaces(roofSurfaces, includeTopSurfaces: true, out ParsedSurface[]? generatedLod2Surfaces))
        {
            return false;
        }

        noWallRoofSlab = cityObject with { Surfaces = generatedLod2Surfaces! };
        return true;
    }

    private static bool IsNoWallLod2BottomInCullBand(
        SurfaceProjectionInfo roofInfo,
        IReadOnlyList<SurfaceProjectionInfo> surfaceInfos)
    {
        double roofMinimumY = roofInfo.MinimumY!.Value;
        SurfaceProjectionInfo[] lowerNonRoofInfos = surfaceInfos
            .Where(static info => info.Surface.Semantic != ParsedSurfaceSemantic.Roof)
            .Where(info => info.MinimumY!.Value < roofMinimumY)
            .ToArray();
        if (lowerNonRoofInfos.Length == 0)
        {
            return false;
        }

        double nearestLowerY = lowerNonRoofInfos
            .Where(info => info.MaximumY!.Value < roofMinimumY)
            .Select(static info => info.MaximumY!.Value)
            .DefaultIfEmpty(double.NegativeInfinity)
            .Max();
        return roofMinimumY - NoWallRoofThicknessMeters <= nearestLowerY + BuildingBottomCullBandMeters;
    }

    private static bool TryGetNoWallTopSurfaces(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian cityObjectCartesian,
        out ParsedSurface[]? topSurfaces)
    {
        topSurfaces = null;
        SurfaceProjectionInfo[] surfaceInfos = cityObject.Surfaces
            .Select(surface => CreateSurfaceProjectionInfo(surface, cityObjectOrigin, cityObjectCartesian))
            .Where(static info => info.MinimumY.HasValue && info.MaximumY.HasValue)
            .ToArray();
        if (surfaceInfos.Length == 0)
        {
            return false;
        }

        double objectMinimumY = surfaceInfos.Min(static info => info.MinimumY!.Value);
        double objectMaximumY = surfaceInfos.Max(static info => info.MaximumY!.Value);
        topSurfaces = surfaceInfos
            .Where(static info => info.IsNearHorizontal)
            .Where(info => info.MaximumY!.Value >= objectMaximumY - 0.1)
            .Where(info => info.MinimumY!.Value > objectMinimumY + BuildingBottomCullBandMeters)
            .Where(info => info.MinimumY!.Value - NoWallRoofThicknessMeters > objectMinimumY + BuildingBottomCullBandMeters)
            .Select(static info => info.Surface)
            .ToArray();
        if (topSurfaces.Length == 0
            || topSurfaces.Any(static surface => surface.InteriorRings.Length != 0))
        {
            topSurfaces = null;
            return false;
        }

        return true;
    }

    private static bool TryCreateNoWallRoofSurfaces(
        IReadOnlyList<ParsedSurface> roofSurfaces,
        bool includeTopSurfaces,
        out ParsedSurface[]? generatedSurfaces)
    {
        generatedSurfaces = null;
        Dictionary<NoWallRoofEdgeKey, int> edgeUseCounts = [];
        Dictionary<string, NoWallRoofRing> ringsByPolygonId = new(StringComparer.Ordinal);
        foreach (ParsedSurface roofSurface in roofSurfaces)
        {
            if (!TryCreateNoWallRoofRing(roofSurface, out NoWallRoofRing ring))
            {
                return false;
            }

            ringsByPolygonId.Add(roofSurface.PolygonId, ring);
            for (int index = 0; index < ring.TopRing.Length; index++)
            {
                int nextIndex = (index + 1) % ring.TopRing.Length;
                NoWallRoofEdgeKey edgeKey = NoWallRoofEdgeKey.Create(ring.TopRing[index], ring.TopRing[nextIndex]);
                edgeUseCounts[edgeKey] = edgeUseCounts.TryGetValue(edgeKey, out int count) ? count + 1 : 1;
            }
        }

        List<ParsedSurface> surfaces = includeTopSurfaces
            ? [.. roofSurfaces.Select(surface => CreateNoWallTopSurface(surface, ringsByPolygonId[surface.PolygonId]))]
            : [];
        foreach (ParsedSurface roofSurface in roofSurfaces)
        {
            NoWallRoofRing ring = ringsByPolygonId[roofSurface.PolygonId];
            surfaces.Add(CreateNoWallBottomSurface(roofSurface, ring));
            for (int index = 0; index < ring.TopRing.Length; index++)
            {
                int nextIndex = (index + 1) % ring.TopRing.Length;
                NoWallRoofEdgeKey edgeKey = NoWallRoofEdgeKey.Create(ring.TopRing[index], ring.TopRing[nextIndex]);
                if (edgeUseCounts[edgeKey] == 1)
                {
                    surfaces.Add(CreateNoWallSideSurface(roofSurface, ring, index, nextIndex));
                }
            }
        }

        generatedSurfaces = surfaces.ToArray();
        return true;
    }

    private static bool TryCreateNoWallRoofRing(
        ParsedSurface roofSurface,
        out NoWallRoofRing ring)
    {
        ring = default;
        GeodeticPoint[] topRing = RemoveClosingPoint(roofSurface.ExteriorRing.Vertices);
        if (topRing.Length < 3)
        {
            return false;
        }

        Float2[]? topUvs = NormalizeUvs(roofSurface.ExteriorRing.UVs, roofSurface.ExteriorRing.Vertices.Length, topRing.Length);
        if (!TryOrientTopRingForDownwardParsedNormal(topRing, topUvs, out GeodeticPoint[]? orientedTopRing, out Float2[]? orientedTopUvs))
        {
            return false;
        }

        GeodeticPoint[] bottomRing = orientedTopRing!.Select(static point => Lower(point, NoWallRoofThicknessMeters)).ToArray();
        ring = new NoWallRoofRing(orientedTopRing!, bottomRing, orientedTopUvs);
        return true;
    }

    private static ParsedSurface CreateNoWallTopSurface(
        ParsedSurface sourceSurface,
        NoWallRoofRing ring)
    {
        GeodeticPoint[] vertices = ring.TopRing;
        Float2[]? uvs = ring.TopUvs;
        return CreateNoWallSurface(sourceSurface, sourceSurface.PolygonId, vertices, uvs, sourceSurface.UsesGeneratedDemTexture);
    }

    private static ParsedSurface CreateNoWallBottomSurface(
        ParsedSurface topSurface,
        NoWallRoofRing ring)
    {
        GeodeticPoint[] vertices = ring.BottomRing.Reverse().ToArray();
        Float2[]? uvs = ring.TopUvs?.Reverse().ToArray();
        string polygonId = $"{topSurface.PolygonId}_generated_no-wall-bottom";
        return CreateNoWallSurface(topSurface, polygonId, vertices, uvs, usesGeneratedDemTexture: false);
    }

    private static ParsedSurface CreateNoWallSideSurface(
        ParsedSurface topSurface,
        NoWallRoofRing ring,
        int index,
        int nextIndex)
    {
        GeodeticPoint[] vertices =
        [
            ring.TopRing[index],
            ring.BottomRing[index],
            ring.BottomRing[nextIndex],
            ring.TopRing[nextIndex],
        ];
        Float2[]? uvs = ring.TopUvs is null
            ? null
            :
            [
                ring.TopUvs[index],
                ring.TopUvs[index],
                ring.TopUvs[nextIndex],
                ring.TopUvs[nextIndex],
        ];
        string polygonId = $"{topSurface.PolygonId}_generated_no-wall-side-{index}";
        return CreateNoWallSurface(topSurface, polygonId, vertices, uvs, usesGeneratedDemTexture: false);
    }

    private static ParsedSurface CreateNoWallSurface(
        ParsedSurface sourceSurface,
        string polygonId,
        GeodeticPoint[] vertices,
        Float2[]? uvs,
        bool usesGeneratedDemTexture)
    {
        GeodeticPoint[] closedVertices = [.. vertices, vertices[0]];
        Float2[]? closedUvs = uvs is null ? null : [.. uvs, uvs[0]];
        return new ParsedSurface(
            polygonId,
            ParsedSurfaceSemantic.Roof,
            new ParsedRing($"{polygonId}-ring", closedVertices, closedUvs),
            InteriorRings: [],
            sourceSurface.BaseColor,
            sourceSurface.TexturePayload,
            usesGeneratedDemTexture,
            sourceSurface.OpticalProperties);
    }

    private static Float2[]? NormalizeUvs(
        IReadOnlyList<Float2>? uvs,
        int sourceVertexCount,
        int ringVertexCount)
    {
        if (uvs is null)
        {
            return null;
        }

        if (uvs.Count == ringVertexCount)
        {
            return uvs.ToArray();
        }

        if (uvs.Count == sourceVertexCount && sourceVertexCount == ringVertexCount + 1)
        {
            return uvs.Take(ringVertexCount).ToArray();
        }

        return null;
    }

    private static bool TryOrientTopRingForDownwardParsedNormal(
        GeodeticPoint[] topRing,
        Float2[]? topUvs,
        out GeodeticPoint[]? orientedTopRing,
        out Float2[]? orientedTopUvs)
    {
        orientedTopRing = topRing;
        orientedTopUvs = topUvs;
        Float3[] positions = CreateApproximatePositions(topRing);
        Float3? normal = PolygonNormal.Compute(positions);
        if (normal is null)
        {
            orientedTopRing = null;
            orientedTopUvs = null;
            return false;
        }

        if (normal.Y <= 0.0)
        {
            return true;
        }

        orientedTopRing = topRing.Reverse().ToArray();
        orientedTopUvs = topUvs?.Reverse().ToArray();
        return true;
    }

    private static Float3[] CreateApproximatePositions(GeodeticPoint[] points)
    {
        if (points.Length == 0)
        {
            return [];
        }

        double referenceLatitude = points.Average(static point => point.Latitude);
        double referenceLongitude = points.Average(static point => point.Longitude);
        return points
            .Select(point => CreateApproximatePosition(point, referenceLatitude, referenceLongitude))
            .ToArray();
    }

    private static bool TryCreateFootprint(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian cityObjectCartesian,
        out Lod1RoofFootprint? footprint,
        bool allowTexturedTop = false,
        double? requiredBottomClearanceMeters = null)
    {
        footprint = null;
        SurfaceProjectionInfo[] surfaceInfos = cityObject.Surfaces
            .Select(surface => CreateSurfaceProjectionInfo(surface, cityObjectOrigin, cityObjectCartesian))
            .Where(static info => info.MinimumY.HasValue && info.MaximumY.HasValue)
            .ToArray();
        if (surfaceInfos.Length == 0)
        {
            return false;
        }

        double objectMinimumY = surfaceInfos.Min(static info => info.MinimumY!.Value);
        double objectMaximumY = surfaceInfos.Max(static info => info.MaximumY!.Value);
        double geometryHeight = objectMaximumY - objectMinimumY;
        SurfaceProjectionInfo[] topCandidates = surfaceInfos
            .Where(static info => info.IsNearHorizontal)
            .Where(info => info.MaximumY!.Value >= objectMaximumY - 0.1)
            .Where(info => info.MinimumY!.Value > objectMinimumY + BuildingBottomCullBandMeters)
            .ToArray();
        if (topCandidates.Length != 1)
        {
            return false;
        }

        if (requiredBottomClearanceMeters.HasValue
            && topCandidates[0].MinimumY!.Value - requiredBottomClearanceMeters.Value <= objectMinimumY + BuildingBottomCullBandMeters)
        {
            return false;
        }

        ParsedSurface topSurface = topCandidates[0].Surface;
        if ((!allowTexturedTop && topSurface.TexturePayload is not null) || topSurface.InteriorRings.Length != 0)
        {
            return false;
        }

        GeodeticPoint[] ring = RemoveClosingPoint(topSurface.ExteriorRing.Vertices);
        if (ring.Length != 4)
        {
            return false;
        }

        Float3[] positions = ring
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        if (!TryClassifyRectangle(positions, out bool firstEdgeIsLongAxis, out double length, out double width))
        {
            return false;
        }

        footprint = new Lod1RoofFootprint(
            topSurface,
            ring,
            length,
            width,
            geometryHeight,
            cityObject.BuildingAttributes ?? BuildingAttributeContext.Empty,
            firstEdgeIsLongAxis);
        return true;
    }

    private static SurfaceProjectionInfo CreateSurfaceProjectionInfo(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian cityObjectCartesian)
    {
        Float3[] positions = surface.Vertices
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        if (positions.Length == 0)
        {
            return new SurfaceProjectionInfo(surface, null, null, false);
        }

        Float3? normal = ComputePolygonNormal(positions);
        bool isNearHorizontal = normal is not null && Math.Abs(normal.Y) >= 0.98;

        return new SurfaceProjectionInfo(
            surface,
            positions.Min(static position => position.Y),
            positions.Max(static position => position.Y),
            isNearHorizontal);
    }

    private static GeodeticPoint[] RemoveClosingPoint(GeodeticPoint[] vertices)
    {
        if (vertices.Length > 1 && AreSamePoint(vertices[0], vertices[^1]))
        {
            return vertices.Take(vertices.Length - 1).ToArray();
        }

        return vertices.ToArray();
    }

    private static bool TryClassifyRectangle(
        Float3[] positions,
        out bool firstEdgeIsLongAxis,
        out double length,
        out double width)
    {
        firstEdgeIsLongAxis = false;
        length = 0.0;
        width = 0.0;
        if (positions.Length != 4)
        {
            return false;
        }

        double[] edges =
        [
            HorizontalDistance(positions[0], positions[1]),
            HorizontalDistance(positions[1], positions[2]),
            HorizontalDistance(positions[2], positions[3]),
            HorizontalDistance(positions[3], positions[0]),
        ];
        if (edges.Any(static edge => edge < 1.0))
        {
            return false;
        }

        if (!ApproximatelyEqual(edges[0], edges[2], 0.15)
            || !ApproximatelyEqual(edges[1], edges[3], 0.15))
        {
            return false;
        }

        Float3 edge0 = NormalizeHorizontal(Subtract(positions[1], positions[0]));
        Float3 edge1 = NormalizeHorizontal(Subtract(positions[2], positions[1]));
        if (Math.Abs(Dot(edge0, edge1)) > 0.15)
        {
            return false;
        }

        firstEdgeIsLongAxis = edges[0] >= edges[1];
        length = Math.Max(edges[0], edges[1]);
        width = Math.Min(edges[0], edges[1]);
        return true;
    }

    private static Float3? ComputePolygonNormal(IEnumerable<Float3> positions)
    {
        Float3[] points = positions.ToArray();
        if (points.Length < 3)
        {
            return null;
        }

        double normalX = 0.0;
        double normalY = 0.0;
        double normalZ = 0.0;

        for (int index = 0; index < points.Length; index++)
        {
            Float3 current = points[index];
            Float3 next = points[(index + 1) % points.Length];
            normalX += (current.Y - next.Y) * (current.Z + next.Z);
            normalY += (current.Z - next.Z) * (current.X + next.X);
            normalZ += (current.X - next.X) * (current.Y + next.Y);
        }

        double magnitude = Math.Sqrt((normalX * normalX) + (normalY * normalY) + (normalZ * normalZ));
        if (magnitude < 1e-8)
        {
            return null;
        }

        return new Float3(normalX / magnitude, normalY / magnitude, normalZ / magnitude);
    }

    private static Float3 CreateScenePosition(
        GeodeticPoint point,
        GeodeticPoint origin,
        LocalCartesian cartesian)
    {
        return SceneAxisMapper.CreatePosition(
            point.Latitude,
            point.Longitude,
            point.Altitude,
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            cartesian);
    }

    private static bool AreSamePoint(GeodeticPoint left, GeodeticPoint right)
    {
        const double tolerance = 1e-8;
        return Math.Abs(left.Latitude - right.Latitude) < tolerance
            && Math.Abs(left.Longitude - right.Longitude) < tolerance
            && Math.Abs(left.Altitude - right.Altitude) < tolerance;
    }

    private static Float3 NormalizeHorizontal(Float3 value)
    {
        Float3 horizontal = new(value.X, 0.0, value.Z);
        double lengthSquared = (horizontal.X * horizontal.X) + (horizontal.Z * horizontal.Z);
        if (lengthSquared < 1e-12)
        {
            return horizontal;
        }

        double length = Math.Sqrt(lengthSquared);
        return new Float3(horizontal.X / length, 0.0, horizontal.Z / length);
    }

    private static double HorizontalDistance(Float3 left, Float3 right)
    {
        double deltaX = left.X - right.X;
        double deltaZ = left.Z - right.Z;
        return Math.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));
    }

    private static bool ApproximatelyEqual(double left, double right, double relativeTolerance)
    {
        double scale = Math.Max(Math.Max(Math.Abs(left), Math.Abs(right)), 1.0);
        return Math.Abs(left - right) <= scale * relativeTolerance;
    }

    private static Float3 Subtract(Float3 left, Float3 right)
    {
        return new Float3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }

    private static GeodeticPoint Lower(GeodeticPoint point, double meters)
    {
        return point with { Altitude = point.Altitude - meters };
    }

    private static Float3 CreateApproximatePosition(
        GeodeticPoint point,
        double referenceLatitude,
        double referenceLongitude)
    {
        const double metersPerLatitudeDegree = 111_320.0;
        double metersPerLongitudeDegree = metersPerLatitudeDegree * Math.Cos(referenceLatitude * (Math.PI / 180.0));
        return new Float3(
            (point.Longitude - referenceLongitude) * metersPerLongitudeDegree,
            point.Altitude,
            (point.Latitude - referenceLatitude) * metersPerLatitudeDegree);
    }

    private static double Dot(Float3 left, Float3 right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }

    private readonly record struct SurfaceProjectionInfo(
        ParsedSurface Surface,
        double? MinimumY,
        double? MaximumY,
        bool IsNearHorizontal);

    private readonly record struct NoWallRoofRing(
        GeodeticPoint[] TopRing,
        GeodeticPoint[] BottomRing,
        Float2[]? TopUvs);

    private readonly record struct NoWallRoofEdgeKey(NoWallRoofPointKey A, NoWallRoofPointKey B)
    {
        public static NoWallRoofEdgeKey Create(GeodeticPoint left, GeodeticPoint right)
        {
            NoWallRoofPointKey leftKey = NoWallRoofPointKey.Create(left);
            NoWallRoofPointKey rightKey = NoWallRoofPointKey.Create(right);
            return leftKey.CompareTo(rightKey) <= 0
                ? new NoWallRoofEdgeKey(leftKey, rightKey)
                : new NoWallRoofEdgeKey(rightKey, leftKey);
        }
    }

    private readonly record struct NoWallRoofPointKey(long Latitude, long Longitude, long Altitude) : IComparable<NoWallRoofPointKey>
    {
        public static NoWallRoofPointKey Create(GeodeticPoint point)
        {
            return new NoWallRoofPointKey(
                Quantize(point.Latitude, 1e8),
                Quantize(point.Longitude, 1e8),
                Quantize(point.Altitude, 1e3));
        }

        public int CompareTo(NoWallRoofPointKey other)
        {
            int latitudeComparison = Latitude.CompareTo(other.Latitude);
            if (latitudeComparison != 0)
            {
                return latitudeComparison;
            }

            int longitudeComparison = Longitude.CompareTo(other.Longitude);
            return longitudeComparison != 0
                ? longitudeComparison
                : Altitude.CompareTo(other.Altitude);
        }

        private static long Quantize(double value, double scale)
        {
            return (long)Math.Round(value * scale, MidpointRounding.AwayFromZero);
        }
    }
}
