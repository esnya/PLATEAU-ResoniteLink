using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using GeographicLib;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class DemTerrainGridProjector
{
    private static readonly Quaternion GridMeshTerrainRotation = new(
        X: Math.Sqrt(0.5),
        Y: 0.0,
        Z: 0.0,
        W: Math.Sqrt(0.5));

    public static bool TryProject(
        ParsedCityObject cityObject,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        PlateauImportRequest request,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
        IDefaultMaterialResolver materialResolver,
        Action<string>? progressReporter,
        CancellationToken cancellationToken,
        out ImportedCityObject? heightMapCityObject)
    {
        cancellationToken.ThrowIfCancellationRequested();
        heightMapCityObject = null;

        if (cityObject.Surfaces.SelectMany(static surface => surface.Vertices).Take(3).Count() < 3)
        {
            return false;
        }

        GeodeticPoint cityObjectOrigin = CityObjectGeometryMetrics.GetCenterOrigin(cityObject);
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

        Float3 slotPosition = SceneAxisMapper.CreatePosition(
            cityObjectOrigin.Latitude,
            cityObjectOrigin.Longitude,
            cityObjectOrigin.Altitude,
            globalOriginPoint.Latitude,
            globalOriginPoint.Longitude,
            globalOriginPoint.Altitude,
            globalCartesian);
        Float3[] positions = cityObject.Surfaces
            .SelectMany(static surface => surface.Vertices)
            .Select(point => CreateGlobalTerrainGridLocalPosition(point, slotPosition, globalOriginPoint, globalCartesian))
            .ToArray();
        TerrainGridTriangle[] triangles = CreateTriangles(cityObject, slotPosition, globalOriginPoint, globalCartesian);
        double seaLevelLocalHeight = CreateGlobalTerrainGridLocalPosition(
            new GeodeticPoint(cityObjectOrigin.Latitude, cityObjectOrigin.Longitude, 0.0),
            slotPosition,
            globalOriginPoint,
            globalCartesian).Y;
        if (positions.Length < 3)
        {
            return false;
        }

        DemTerrainGridBounds heightMapBounds = DemTerrainGridBoundsFactory.Create(
            positions,
            GetCityObjectGeographicBounds(cityObject),
            cityObjectOrigin.Latitude,
            cityObjectOrigin.Longitude,
            cityObjectOrigin.Altitude,
            demTerrainTextureOverlay,
            (latitude, longitude, altitude) => CreateGlobalTerrainGridLocalPosition(
                new GeodeticPoint(latitude, longitude, altitude),
                slotPosition,
                globalOriginPoint,
                globalCartesian));
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

        TerrainGridHeightSampler heightSampler = TerrainGridHeightSampler.Create(
            triangles,
            minX,
            maxX,
            minZ,
            maxZ);

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
        double[] localHeights = new double[width * height];
        bool[] sampledInsideTriangles = new bool[width * height];

        for (int zIndex = 0; zIndex < height; zIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double v = height == 1 ? 0.0 : (double)zIndex / (height - 1);
            double sampleZ = minZ + (extentZ * v);
            for (int xIndex = 0; xIndex < width; xIndex++)
            {
                double u = width == 1 ? 0.0 : (double)xIndex / (width - 1);
                double sampleX = minX + (extentX * u);
                int sampleIndex = (zIndex * width) + xIndex;
                if (heightSampler.TrySampleHeight(sampleX, sampleZ, out double localHeight))
                {
                    localHeights[sampleIndex] = localHeight;
                    sampledInsideTriangles[sampleIndex] = true;
                }
                else
                {
                    localHeights[sampleIndex] = seaLevelLocalHeight;
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        DemTerrainGridMissingHeightSampleFiller.ExtendBoundaryConnectedMissingSamples(localHeights, sampledInsideTriangles, width, height);
        double minHeight = localHeights.Min();
        double maxHeight = localHeights.Max();

        DemTerrainGridMaterialPlan materialPlan = CreateMaterialPlan(
            cityObject,
            cityObjectOrigin,
            cityObjectCartesian,
            demTerrainTextureOverlay,
            request.MeshCode,
            requestedMeshAreas,
            materialResolver);
        if (materialPlan.Materials.Length == 0)
        {
            return false;
        }

        Float2? heightMapUvScale = materialPlan.OccupiedUvRect.HasValue
            ? ToContractFloat2(materialPlan.OccupiedUvRect.Value.ScaleValue)
            : null;
        Float2? heightMapUvOffset = materialPlan.OccupiedUvRect.HasValue
            ? ToContractFloat2(materialPlan.OccupiedUvRect.Value.OffsetValue)
            : null;

        Float3 adjustedSlotPosition = slotPosition with
        {
            X = slotPosition.X + centerX,
            Y = slotPosition.Y + maxHeight,
            Z = slotPosition.Z + centerZ,
        };

        heightMapCityObject = new ImportedCityObject(
            ObjectKey: cityObject.SlotKey,
            DisplayName: cityObject.DisplayName,
            PackageName: cityObject.PackageName,
            ActualMeshCode: cityObject.ActualMeshCode,
            LodLevel: cityObject.LodLevel,
            Transform: new Transform3D(
                ToContractFloat3(adjustedSlotPosition),
                ToContractQuaternion(GridMeshTerrainRotation)),
            Geometry: new TerrainGridGeometry(
                Width: width,
                Height: height,
                Size: new Float2(extentX, extentZ),
                MinHeight: minHeight,
                MaxHeight: maxHeight,
                HeightSamples: localHeights,
                UvScale: heightMapUvScale,
                UvOffset: heightMapUvOffset),
            Materials: materialPlan.Materials,
            SourceFileRelativePath: cityObject.SourceFileRelativePath);
        return true;
    }

    public static DemTerrainGridMaterialPlan CreateMaterialPlan(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        string requestedMeshCode,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas,
        IDefaultMaterialResolver materialResolver)
    {
        ParsedSurface[] projectionSurfaces =
            cityObject.Surfaces.Select(static surface => surface).ToArray();
        HashSet<string> culledSurfaceIds = BottomBandSurfaceCuller.GetCulledSurfaceIds(
            cityObject.PackageName,
            projectionSurfaces,
            cityObjectOrigin,
            cityObjectCartesian);
        return DemTerrainGridMaterialPlanner.Create(
            cityObject,
            culledSurfaceIds,
            cityObjectOrigin,
            cityObjectCartesian,
            demTerrainTextureOverlay,
            requestedMeshCode,
            requestedMeshAreas,
            materialResolver);
    }

    private static GeographicRectangle GetCityObjectGeographicBounds(ParsedCityObject cityObject)
    {
        List<GeodeticPoint> vertices = cityObject.Surfaces.SelectMany(static surface => surface.Vertices).ToList();
        return new GeographicRectangle(
            MinLatitude: vertices.Min(static point => point.Latitude),
            MaxLatitude: vertices.Max(static point => point.Latitude),
            MinLongitude: vertices.Min(static point => point.Longitude),
            MaxLongitude: vertices.Max(static point => point.Longitude));
    }

    private static TerrainGridTriangle[] CreateTriangles(
        ParsedCityObject cityObject,
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

    private static Float3 CreateGlobalTerrainGridLocalPosition(
        GeodeticPoint point,
        Float3 slotPosition,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian)
    {
        Float3 globalPosition = SceneAxisMapper.CreatePosition(
            point.Latitude,
            point.Longitude,
            point.Altitude,
            globalOriginPoint.Latitude,
            globalOriginPoint.Longitude,
            globalOriginPoint.Altitude,
            globalCartesian);
        return new Float3(
            globalPosition.X - slotPosition.X,
            globalPosition.Y - slotPosition.Y,
            globalPosition.Z - slotPosition.Z);
    }

    private static Float2 ToContractFloat2(ScalarPair value) => new(value.X, value.Y);

    private static Float3 ToContractFloat3(Float3 value) => new(value.X, value.Y, value.Z);

    private static Quaternion ToContractQuaternion(Quaternion value) => new(value.X, value.Y, value.Z, value.W);
}
