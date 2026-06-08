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
    private static readonly Quaternion GridMeshTerrainRotation = new(
        X: Math.Sqrt(0.5),
        Y: 0.0,
        Z: 0.0,
        W: Math.Sqrt(0.5));

    internal static bool TryProject(
        ConstructionCityObjectDraft cityObject,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        PlateauImportRequest request,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        ResolveDefaultMaterial materialResolver,
        Action<string>? progressReporter,
        CancellationToken cancellationToken,
        out TerrainGridProjectedCityObject? heightMapCityObject)
    {
        cancellationToken.ThrowIfCancellationRequested();
        heightMapCityObject = null;

        if (cityObject.Surfaces.SelectMany(static surface => surface.Vertices).Take(3).Count() < 3)
        {
            return false;
        }

        GeodeticPoint cityObjectOrigin = ResolveCityObjectOrigin(cityObject);
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
        Float3[] positions = cityObject.Surfaces
            .SelectMany(static surface => surface.Vertices)
            .Select(point => CreateGlobalTerrainGridLocalPosition(point, slotPosition, globalOriginPoint, globalCartesian))
            .ToArray();
        TerrainGridTriangle[] triangles = CreateDemTerrainGridTriangles(cityObject, slotPosition, globalOriginPoint, globalCartesian);
        double seaLevelLocalHeight = CreateGlobalTerrainGridLocalPosition(
            new GeodeticPoint(cityObjectOrigin.Latitude, cityObjectOrigin.Longitude, 0.0),
            slotPosition,
            globalOriginPoint,
            globalCartesian).Y;
        if (positions.Length < 3)
        {
            return false;
        }

        DemTerrainGridBounds heightMapBounds = CreateDemTerrainGridBounds(
            cityObject,
            slotPosition,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlay,
            positions);
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
            cancellationToken);
        width = heightSamples.Width;
        height = heightSamples.Height;
        double[] localHeights = heightSamples.LocalHeights;
        TerrainGridSampleCoverage[] sampleCoverage = heightSamples.SampleCoverage;
        double minHeight = localHeights.Min();
        double maxHeight = localHeights.Max();

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
            HeightSamples: localHeights,
            SampleCoverage: sampleCoverage,
            UvScale: heightMapUvScale,
            UvOffset: heightMapUvOffset);
        ImportedCityObject projectedCityObject = new(
            ObjectKey: cityObject.SlotKey,
            DisplayName: cityObject.DisplayName,
            PackageName: cityObject.PackageName,
            ActualMeshCode: cityObject.ActualMeshCode,
            LodLevel: cityObject.LodLevel,
            Transform: new Transform3D(
                ToContractFloat3(adjustedSlotPosition),
                ToContractQuaternion(GridMeshTerrainRotation)),
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
        ResolveDefaultMaterial materialResolver)
    {
        if (demTerrainTextureOverlay is null)
        {
            return null;
        }

        GeographicRectangle? demObjectBounds = TryGetDemObjectGeographicBounds(cityObject.Source, demTerrainTextureOverlay);
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
            demObjectBounds);
        return occupiedUvRect is { IsIdentity: true } ? null : occupiedUvRect;
    }

    private static GeographicRectangle? TryGetDemObjectGeographicBounds(
        ParsedCityObject cityObject,
        TerrainTextureOverlay? demTerrainTextureOverlay)
    {
        if (demTerrainTextureOverlay is null
            || !string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return ResolveCityObjectGeographicBounds(cityObject);
    }

    private static DemTerrainGridBounds CreateDemTerrainGridBounds(
        ConstructionCityObjectDraft cityObject,
        Float3 slotPosition,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IReadOnlyList<Float3> positions)
    {
        double rawMinX = positions.Min(static position => position.X);
        double rawMaxX = positions.Max(static position => position.X);
        double rawMinZ = positions.Min(static position => position.Z);
        double rawMaxZ = positions.Max(static position => position.Z);

        if (demTerrainTextureOverlay is null)
        {
            return new DemTerrainGridBounds(rawMinX, rawMaxX, rawMinZ, rawMaxZ);
        }

        GeographicRectangle clippedBounds = IntersectGeographicBounds(
            ResolveCityObjectGeographicBounds(cityObject.Source),
            demTerrainTextureOverlay.GeographicBounds);
        Float3[] clippedAxisPositions = CreateClippedAxisPositions(
            clippedBounds,
            slotPosition,
            globalOriginPoint,
            globalCartesian);

        double clippedMinX = Math.Min(clippedAxisPositions[0].X, clippedAxisPositions[1].X);
        double clippedMaxX = Math.Max(clippedAxisPositions[0].X, clippedAxisPositions[1].X);
        double clippedMinZ = Math.Min(clippedAxisPositions[2].Z, clippedAxisPositions[3].Z);
        double clippedMaxZ = Math.Max(clippedAxisPositions[2].Z, clippedAxisPositions[3].Z);

        clippedMinX = Math.Max(clippedMinX, rawMinX);
        clippedMaxX = Math.Min(clippedMaxX, rawMaxX);
        clippedMinZ = Math.Max(clippedMinZ, rawMinZ);
        clippedMaxZ = Math.Min(clippedMaxZ, rawMaxZ);

        if ((clippedMaxX - clippedMinX) <= 1e-6 || (clippedMaxZ - clippedMinZ) <= 1e-6)
        {
            return new DemTerrainGridBounds(rawMinX, rawMaxX, rawMinZ, rawMaxZ);
        }

        return new DemTerrainGridBounds(clippedMinX, clippedMaxX, clippedMinZ, clippedMaxZ);
    }

    private static Float3[] CreateClippedAxisPositions(
        GeographicRectangle clippedBounds,
        Float3 slotPosition,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian)
    {
        double boundsAltitude = globalOriginPoint.Altitude;
        double referenceLatitude = globalOriginPoint.Latitude;
        double referenceLongitude = globalOriginPoint.Longitude;
        // GridMesh bounds are axis-aligned in scene space. Project the geographic
        // axes from one scene-wide frame so adjacent chunks quantize the same
        // latitude or longitude boundary to the same local X/Z edge.
        return
        [
            CreateGlobalTerrainGridLocalPosition(
                new GeodeticPoint(referenceLatitude, clippedBounds.MinLongitude, boundsAltitude),
                slotPosition,
                globalOriginPoint,
                globalCartesian),
            CreateGlobalTerrainGridLocalPosition(
                new GeodeticPoint(referenceLatitude, clippedBounds.MaxLongitude, boundsAltitude),
                slotPosition,
                globalOriginPoint,
                globalCartesian),
            CreateGlobalTerrainGridLocalPosition(
                new GeodeticPoint(clippedBounds.MinLatitude, referenceLongitude, boundsAltitude),
                slotPosition,
                globalOriginPoint,
                globalCartesian),
            CreateGlobalTerrainGridLocalPosition(
                new GeodeticPoint(clippedBounds.MaxLatitude, referenceLongitude, boundsAltitude),
                slotPosition,
                globalOriginPoint,
                globalCartesian),
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

    private static TerrainGridTriangle[] CreateDemTerrainGridTriangles(
        ConstructionCityObjectDraft cityObject,
        Float3 slotPosition,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian)
    {
        List<TerrainGridTriangle> triangles = [];
        foreach (ParsedSurface surface in cityObject.Surfaces)
        {
            Float3[] positions = surface.ExteriorRing.Vertices
                .Select(point => CreateGlobalTerrainGridLocalPosition(point, slotPosition, globalOriginPoint, globalCartesian))
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

    private static GeodeticPoint ResolveCityObjectOrigin(ConstructionCityObjectDraft cityObject)
    {
        return CityObjectOriginResolver.Resolve(
            cityObject.GeodeticOriginOverride,
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices));
    }

    private static Float3 CreateGlobalTerrainGridLocalPosition(
        GeodeticPoint point,
        Float3 slotPosition,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian)
    {
        Float3 globalPosition = CreateScenePosition(point, globalOriginPoint, globalCartesian);
        return new Float3(
            globalPosition.X - slotPosition.X,
            globalPosition.Y - slotPosition.Y,
            globalPosition.Z - slotPosition.Z);
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

    private static Quaternion ToContractQuaternion(Quaternion value) => new(value.X, value.Y, value.Z, value.W);

    private sealed record DemTerrainGridBounds(
        double MinX,
        double MaxX,
        double MinZ,
        double MaxZ);
}

internal sealed record TerrainGridProjectedCityObject(
    ImportedCityObject CityObject,
    TerrainGridGeometry Geometry);
