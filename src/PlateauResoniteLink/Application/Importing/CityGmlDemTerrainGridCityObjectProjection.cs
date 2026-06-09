using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;

using LocalCartesian = GeographicLib.LocalCartesian;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityGmlDemTerrainGridCityObjectProjection
{
    internal static bool TryProject(
        ConstructionCityObjectDraft cityObject,
        ConstructionCityObjectDraft terrainGridSamplingSource,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        PlateauImportRequest request,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        ResolveDefaultMaterial materialResolver,
        Action<string>? progressReporter,
        CancellationToken cancellationToken,
        out bool outsideSamplingBounds,
        out TerrainGridProjectedCityObject? heightMapCityObject)
    {
        cancellationToken.ThrowIfCancellationRequested();
        outsideSamplingBounds = false;
        heightMapCityObject = null;

        if (terrainGridSamplingSource.Surfaces.SelectMany(static surface => surface.Vertices).Take(3).Count() < 3)
        {
            return false;
        }

        GeodeticPoint cityObjectOrigin = DemTerrainObjectFrameResolver.ResolveRequiredThirdMeshOrigin(
            cityObject.ActualMeshCode,
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices));
        LocalCartesian? cityObjectCartesian = cityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                cityObject.ReferenceSystem.Geocentric)
            : null;
        if (cityObjectCartesian is null)
        {
            return false;
        }

        Float3 slotPosition = CreateScenePosition(cityObjectOrigin, globalOriginPoint, globalCartesian);
        Float3[] positions = terrainGridSamplingSource.Surfaces
            .SelectMany(static surface => surface.Vertices)
            .Select(point => CreateTerrainGridLocalPosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        TerrainGridTriangle[] triangles = CreateDemTerrainGridTriangles(terrainGridSamplingSource, cityObjectOrigin, cityObjectCartesian);
        double seaLevelLocalHeight = CreateTerrainGridLocalPosition(
            new GeodeticPoint(cityObjectOrigin.Latitude, cityObjectOrigin.Longitude, 0.0),
            cityObjectOrigin,
            cityObjectCartesian).Y;
        if (positions.Length < 3)
        {
            return false;
        }

        if (!TryCreateDemTerrainGridBounds(
            cityObject,
            terrainGridSamplingSource,
            cityObjectOrigin,
            cityObjectCartesian,
            demTerrainTextureOverlay,
            requestedMeshCodeBounds,
            positions,
            out DemTerrainGridBounds? heightMapBounds)
            || heightMapBounds is null)
        {
            outsideSamplingBounds = true;
            return false;
        }

        double minX = heightMapBounds.MinX;
        double maxX = heightMapBounds.MaxX;
        double minZ = heightMapBounds.MinZ;
        double maxZ = heightMapBounds.MaxZ;
        double centerX = (minX + maxX) / 2.0;
        double centerZ = (minZ + maxZ) / 2.0;
        double extentX = maxX - minX;
        double extentZ = maxZ - minZ;
        if (extentX <= 1e-6 || extentZ <= 1e-6)
        {
            return false;
        }

        int width = Math.Clamp(
            (int)Math.Ceiling(extentX / request.TerrainGridMetersPerVertex) + 1,
            2,
            request.TerrainGridMaxResolution);
        int height = Math.Clamp(
            (int)Math.Ceiling(extentZ / request.TerrainGridMetersPerVertex) + 1,
            2,
            request.TerrainGridMaxResolution);
        progressReporter?.Invoke(
            PlateauLog.Debug(
                "import",
                $"Sampling DEM terrain grid '{cityObject.SlotKey}' "
                + $"(width={width}, height={height}, triangles={triangles.Length})."));

        DemTerrainGridHeightSamples heightSamples = CityGmlDemTerrainGridSampler.Sample(
            minX,
            maxX,
            minZ,
            maxZ,
            request.TerrainGridMetersPerVertex,
            request.TerrainGridMaxResolution,
            seaLevelLocalHeight,
            triangles,
            CreateSeaLevelFallbackHeightProvider(slotPosition, cityObjectCartesian, globalCartesian),
            cancellationToken);
        width = heightSamples.Width;
        height = heightSamples.Height;
        double[] sourceLocalHeights = heightSamples.LocalHeights;
        TerrainGridSampleCoverage[] sampleCoverage = heightSamples.SampleCoverage;
        double minHeight = sourceLocalHeights.Min();
        double maxHeight = sourceLocalHeights.Max();

        MaterialBinding[] materials = CityGmlSurfaceMaterialResolver.CreateDemTerrainGridMaterials(
            cityObject,
            cityObjectOrigin,
            cityObjectCartesian,
            demTerrainTextureOverlay,
            materialResolver);
        if (materials.Length == 0)
        {
            return false;
        }

        TextureUvRect? heightMapOccupiedUvRect = TryCreateDemTerrainGridOccupiedUvRect(
            cityObject,
            cityObjectOrigin,
            cityObjectCartesian,
            demTerrainTextureOverlay,
            heightMapBounds.GeographicBounds,
            materialResolver);
        Float2? heightMapUvScale = heightMapOccupiedUvRect.HasValue
            ? ToContractFloat2(heightMapOccupiedUvRect.Value.ScaleValue)
            : null;
        Float2? heightMapUvOffset = heightMapOccupiedUvRect.HasValue
            ? ToContractFloat2(heightMapOccupiedUvRect.Value.OffsetValue)
            : null;

        Float3 adjustedSlotPosition = slotPosition with
        {
            X = slotPosition.X + centerX,
            Y = slotPosition.Y + maxHeight,
            Z = slotPosition.Z + centerZ,
        };

        TerrainGridGeometry geometry = new(
            Width: width,
            Height: height,
            Size: new Float2(extentX, extentZ),
            MinHeight: minHeight,
            MaxHeight: maxHeight,
            HeightSamples: sourceLocalHeights,
            SampleCoverage: sampleCoverage,
            UvScale: heightMapUvScale,
            UvOffset: heightMapUvOffset);
        ImportedCityObject projectedCityObject = new(
            ObjectKey: cityObject.SlotKey,
            DisplayName: cityObject.DisplayName,
            PackageName: cityObject.PackageName,
            ActualMeshCode: cityObject.ActualMeshCode,
            LodLevel: cityObject.LodLevel,
            Transform: new Transform3D(ToContractFloat3(adjustedSlotPosition)),
            Geometry: geometry,
            Materials: materials,
            SourceFileRelativePath: cityObject.SourceFileRelativePath);
        heightMapCityObject = new TerrainGridProjectedCityObject(projectedCityObject, geometry);
        return true;
    }

    private static TextureUvRect? TryCreateDemTerrainGridOccupiedUvRect(
        ConstructionCityObjectDraft cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        GeographicRectangle terrainGridGeographicBounds,
        ResolveDefaultMaterial materialResolver)
    {
        if (demTerrainTextureOverlay is null)
        {
            return null;
        }

        ResolvedSurfaceMaterial? representativeSurface = CityGmlSurfaceMaterialResolver.EnumerateSurfaces(
                cityObject,
                cityObjectOrigin,
                cityObjectCartesian,
                demTerrainTextureOverlay,
                materialResolver)
            .FirstOrDefault(static resolvedSurface => resolvedSurface.Material.TerrainOverlay is not null);
        if (representativeSurface is null)
        {
            return null;
        }

        TextureUvRect? occupiedUvRect = DemTerrainOverlayUvMapper.TryCreateTerrainGridOccupiedUvRect(
            cityObject.Source,
            representativeSurface,
            demTerrainTextureOverlay,
            terrainGridGeographicBounds);
        return occupiedUvRect is { IsIdentity: true } ? null : occupiedUvRect;
    }

    private static bool TryCreateDemTerrainGridBounds(
        ConstructionCityObjectDraft cityObject,
        ConstructionCityObjectDraft terrainGridSamplingSource,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        IReadOnlyList<Float3> positions,
        out DemTerrainGridBounds? bounds)
    {
        bounds = null;
        double rawMinX = positions.Min(static position => position.X);
        double rawMaxX = positions.Max(static position => position.X);
        double rawMinZ = positions.Min(static position => position.Z);
        double rawMaxZ = positions.Max(static position => position.Z);

        GeographicRectangle clippedBounds = ResolveDemTerrainGridGeographicBounds(
            cityObject,
            demTerrainTextureOverlay,
            requestedMeshCodeBounds);
        GeographicRectangle rawGeographicBounds = ResolveCityObjectGeographicBounds(terrainGridSamplingSource.Source);
        GeographicRectangle clampedGeographicBounds = IntersectGeographicBounds(clippedBounds, rawGeographicBounds);
        bool rawNearlyCoversClippedBounds = NearlyCoversGeographicBounds(rawGeographicBounds, clippedBounds);
        if (rawNearlyCoversClippedBounds)
        {
            clampedGeographicBounds = clippedBounds;
        }

        if (!IsUsableGeographicBounds(clampedGeographicBounds))
        {
            clampedGeographicBounds = rawGeographicBounds;
        }

        Float3[] clippedAxisPositions = CreateClippedAxisPositions(
            clippedBounds,
            cityObjectOrigin,
            cityObjectCartesian);

        double clippedMinX = Math.Min(clippedAxisPositions[0].X, clippedAxisPositions[1].X);
        double clippedMaxX = Math.Max(clippedAxisPositions[0].X, clippedAxisPositions[1].X);
        double clippedMinZ = Math.Min(clippedAxisPositions[2].Z, clippedAxisPositions[3].Z);
        double clippedMaxZ = Math.Max(clippedAxisPositions[2].Z, clippedAxisPositions[3].Z);

        if (!rawNearlyCoversClippedBounds)
        {
            clippedMinX = Math.Max(clippedMinX, rawMinX);
            clippedMaxX = Math.Min(clippedMaxX, rawMaxX);
            clippedMinZ = Math.Max(clippedMinZ, rawMinZ);
            clippedMaxZ = Math.Min(clippedMaxZ, rawMaxZ);
        }

        if ((clippedMaxX - clippedMinX) <= 1e-6 || (clippedMaxZ - clippedMinZ) <= 1e-6)
        {
            if (cityObject.Source.SharedAcrossMeshCodes)
            {
                return false;
            }

            bounds = new DemTerrainGridBounds(rawMinX, rawMaxX, rawMinZ, rawMaxZ, clampedGeographicBounds);
            return true;
        }

        bounds = new DemTerrainGridBounds(clippedMinX, clippedMaxX, clippedMinZ, clippedMaxZ, clampedGeographicBounds);
        return true;
    }

    private static GeographicRectangle ResolveDemTerrainGridGeographicBounds(
        ConstructionCityObjectDraft cityObject,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds)
    {
        GeographicRectangle bounds = TryCreateThirdMeshGeographicBounds(cityObject.ActualMeshCode, out GeographicRectangle thirdMeshBounds)
            ? thirdMeshBounds
            : ResolveCityObjectGeographicBounds(cityObject.Source);

        if (requestedMeshCodeBounds.Count > 0)
        {
            bounds = IntersectRequestedMeshBounds(bounds, requestedMeshCodeBounds)
                ?? bounds;
        }

        return demTerrainTextureOverlay is null
            ? bounds
            : IntersectGeographicBounds(bounds, demTerrainTextureOverlay.GeographicBounds);
    }

    private static GeographicRectangle? IntersectRequestedMeshBounds(
        GeographicRectangle bounds,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds)
    {
        GeographicRectangle[] intersections = requestedMeshCodeBounds
            .Select(requested => IntersectGeographicBounds(bounds, new GeographicRectangle(
                requested.SouthLatitude,
                requested.NorthLatitude,
                requested.WestLongitude,
                requested.EastLongitude)))
            .Where(static intersection => IsUsableGeographicBounds(intersection))
            .ToArray();
        return intersections.Length == 0
            ? null
            : new GeographicRectangle(
                intersections.Min(static intersection => intersection.MinLatitude),
                intersections.Max(static intersection => intersection.MaxLatitude),
                intersections.Min(static intersection => intersection.MinLongitude),
                intersections.Max(static intersection => intersection.MaxLongitude));
    }

    private static bool TryCreateThirdMeshGeographicBounds(
        string actualMeshCode,
        out GeographicRectangle bounds)
    {
        if (ThirdRegionalMeshCode.TryParse(actualMeshCode, out ThirdRegionalMeshCode thirdMeshCode))
        {
            JisRegionalMeshBounds meshBounds = thirdMeshCode.Bounds;
            bounds = new GeographicRectangle(
                meshBounds.SouthLatitude,
                meshBounds.NorthLatitude,
                meshBounds.WestLongitude,
                meshBounds.EastLongitude);
            return true;
        }

        bounds = new GeographicRectangle(0.0, 0.0, 0.0, 0.0);
        return false;
    }

    private static Float3[] CreateClippedAxisPositions(
        GeographicRectangle clippedBounds,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian cityObjectCartesian)
    {
        double boundsAltitude = cityObjectOrigin.Altitude;
        double referenceLatitude = cityObjectOrigin.Latitude;
        double referenceLongitude = cityObjectOrigin.Longitude;
        return
        [
            CreateTerrainGridLocalPosition(
                new GeodeticPoint(referenceLatitude, clippedBounds.MinLongitude, boundsAltitude),
                cityObjectOrigin,
                cityObjectCartesian),
            CreateTerrainGridLocalPosition(
                new GeodeticPoint(referenceLatitude, clippedBounds.MaxLongitude, boundsAltitude),
                cityObjectOrigin,
                cityObjectCartesian),
            CreateTerrainGridLocalPosition(
                new GeodeticPoint(clippedBounds.MinLatitude, referenceLongitude, boundsAltitude),
                cityObjectOrigin,
                cityObjectCartesian),
            CreateTerrainGridLocalPosition(
                new GeodeticPoint(clippedBounds.MaxLatitude, referenceLongitude, boundsAltitude),
                cityObjectOrigin,
                cityObjectCartesian),
        ];
    }

    private static GeographicRectangle ResolveCityObjectGeographicBounds(ParsedCityObject cityObject)
    {
        return CityObjectGeographicBoundsResolver.Resolve(
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices));
    }

    private static GeographicRectangle IntersectGeographicBounds(
        GeographicRectangle left,
        GeographicRectangle right)
    {
        return new GeographicRectangle(
            MinLatitude: Math.Max(left.MinLatitude, right.MinLatitude),
            MaxLatitude: Math.Min(left.MaxLatitude, right.MaxLatitude),
            MinLongitude: Math.Max(left.MinLongitude, right.MinLongitude),
            MaxLongitude: Math.Min(left.MaxLongitude, right.MaxLongitude));
    }

    private static bool IsUsableGeographicBounds(GeographicRectangle bounds)
    {
        return (bounds.MaxLatitude - bounds.MinLatitude) > 1e-12
            && (bounds.MaxLongitude - bounds.MinLongitude) > 1e-12;
    }

    private static bool NearlyCoversGeographicBounds(
        GeographicRectangle candidate,
        GeographicRectangle bounds)
    {
        const double relativeTolerance = 1e-3;
        double latitudeTolerance = Math.Max(bounds.MaxLatitude - bounds.MinLatitude, 0.0) * relativeTolerance;
        double longitudeTolerance = Math.Max(bounds.MaxLongitude - bounds.MinLongitude, 0.0) * relativeTolerance;
        return candidate.MinLatitude <= bounds.MinLatitude + latitudeTolerance
            && candidate.MaxLatitude >= bounds.MaxLatitude - latitudeTolerance
            && candidate.MinLongitude <= bounds.MinLongitude + longitudeTolerance
            && candidate.MaxLongitude >= bounds.MaxLongitude - longitudeTolerance;
    }

    private static TerrainGridTriangle[] CreateDemTerrainGridTriangles(
        ConstructionCityObjectDraft cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian cityObjectCartesian)
    {
        List<TerrainGridTriangle> triangles = [];
        foreach (ParsedSurface surface in cityObject.Surfaces)
        {
            Float3[] positions = surface.ExteriorRing.Vertices
                .Select(point => CreateTerrainGridLocalPosition(point, cityObjectOrigin, cityObjectCartesian))
                .ToArray();
            if (positions.Length < 3)
            {
                continue;
            }

            Float3 origin = positions[0];
            for (int index = 1; index + 1 < positions.Length; index++)
            {
                triangles.Add(new TerrainGridTriangle(origin, positions[index], positions[index + 1]));
            }
        }

        return triangles.ToArray();
    }

    private static Func<double, double, double>? CreateSeaLevelFallbackHeightProvider(
        Float3 slotPosition,
        LocalCartesian cityObjectCartesian,
        LocalCartesian? globalCartesian)
    {
        if (globalCartesian is null)
        {
            return null;
        }

        return (localX, localZ) =>
        {
            (double latitude, double longitude, _) = cityObjectCartesian.Reverse(
                localX,
                localZ,
                0.0);
            (_, _, double seaLevelUp) = globalCartesian.Forward(
                latitude,
                longitude,
                0.0);
            return seaLevelUp - slotPosition.Y;
        };
    }

    private static Float3 CreateTerrainGridLocalPosition(
        GeodeticPoint point,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian cityObjectCartesian)
    {
        return CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian);
    }

    private static Float3 CreateScenePosition(
        GeodeticPoint point,
        GeodeticPoint origin,
        LocalCartesian? cartesian)
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

    private static Float2 ToContractFloat2(ScalarPair value) => new(value.X, value.Y);

    private static Float3 ToContractFloat3(Float3 value) => new(value.X, value.Y, value.Z);

    private sealed record DemTerrainGridBounds(
        double MinX,
        double MaxX,
        double MinZ,
        double MaxZ,
        GeographicRectangle GeographicBounds);
}

internal sealed record TerrainGridProjectedCityObject(
    ImportedCityObject CityObject,
    TerrainGridGeometry Geometry);
