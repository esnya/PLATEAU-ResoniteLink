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
        ParsedCityObject cityObject,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        PlateauImportRequest request,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
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

        ConstructionCityObjectDraft draft = ConstructionCityObjectDraft.FromParsedCityObject(cityObject);
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
            cityObjectOrigin,
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
            draft,
            cityObjectOrigin,
            cityObjectCartesian,
            demTerrainTextureOverlay,
            request.MeshCode,
            requestedMeshCodeBounds,
            materialResolver);
        if (materials.Length == 0)
        {
            return false;
        }

        TextureUvRect? heightMapOccupiedUvRect = TryCreateDemTerrainGridOccupiedUvRect(
            draft,
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
                SampleCoverage: sampleCoverage,
                UvScale: heightMapUvScale,
                UvOffset: heightMapUvOffset),
            Materials: materials,
            SourceFileRelativePath: cityObject.SourceFileRelativePath);
        return true;
    }

    private static TextureUvRect? TryCreateDemTerrainGridOccupiedUvRect(
        ConstructionCityObjectDraft cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
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
            .FirstOrDefault(static resolvedSurface => resolvedSurface.Surface.UsesGeneratedDemTexture);
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
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
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
            ResolveCityObjectGeographicBounds(cityObject),
            demTerrainTextureOverlay.GeographicBounds);
        double referenceLatitude = cityObjectOrigin.Latitude;
        double referenceLongitude = cityObjectOrigin.Longitude;
        Float3 westPosition = CreateGlobalTerrainGridLocalPosition(
            new GeodeticPoint(referenceLatitude, clippedBounds.MinLongitude, cityObjectOrigin.Altitude),
            slotPosition,
            globalOriginPoint,
            globalCartesian);
        Float3 eastPosition = CreateGlobalTerrainGridLocalPosition(
            new GeodeticPoint(referenceLatitude, clippedBounds.MaxLongitude, cityObjectOrigin.Altitude),
            slotPosition,
            globalOriginPoint,
            globalCartesian);
        Float3 southPosition = CreateGlobalTerrainGridLocalPosition(
            new GeodeticPoint(clippedBounds.MinLatitude, referenceLongitude, cityObjectOrigin.Altitude),
            slotPosition,
            globalOriginPoint,
            globalCartesian);
        Float3 northPosition = CreateGlobalTerrainGridLocalPosition(
            new GeodeticPoint(clippedBounds.MaxLatitude, referenceLongitude, cityObjectOrigin.Altitude),
            slotPosition,
            globalOriginPoint,
            globalCartesian);

        double clippedMinX = Math.Min(westPosition.X, eastPosition.X);
        double clippedMaxX = Math.Max(westPosition.X, eastPosition.X);
        double clippedMinZ = Math.Min(southPosition.Z, northPosition.Z);
        double clippedMaxZ = Math.Max(southPosition.Z, northPosition.Z);

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

    private static GeodeticPoint ResolveCityObjectOrigin(ParsedCityObject cityObject)
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
