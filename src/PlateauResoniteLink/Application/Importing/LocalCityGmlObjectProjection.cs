using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;

using Geocentric = GeographicLib.Geocentric;
using LocalCartesian = GeographicLib.LocalCartesian;

namespace PlateauResoniteLink.Application.Importing;

internal static class LocalCityGmlObjectProjection
{
    public const string DefaultDemTerrainTexturePath = DemTerrainTextureDefaults.PlateauOrthoPath;
    public const string DefaultDemTerrainTextureUrlTemplate = DemTerrainTextureDefaults.PlateauOrthoUrlTemplate;
    public const string DefaultDemTerrainTextureFallbackUrlTemplate = DemTerrainTextureDefaults.GsiFallbackUrlTemplate;
    public const int DefaultDemTerrainTextureZoomLevel = DemTerrainTextureDefaults.PlateauOrthoZoomLevel;
    public const int DefaultDemTerrainTextureFallbackZoomLevel = DemTerrainTextureDefaults.FallbackZoomLevel;
    public const int DefaultDemTerrainTextureMaxSize = DemTerrainTextureDefaults.MaxTextureSize;
    public const double DefaultGeneratedRoadMarkingWidthMeters = GeneratedRoadMarkingCityObjectFactory.DefaultMarkingWidthMeters;
    public const double DefaultGeneratedRoadMarkingSegmentLengthMeters = GeneratedRoadMarkingCityObjectFactory.DefaultSegmentLengthMeters;
    public const double DefaultTerrainAlignedTransportationSegmentLengthMeters = TerrainAlignedTransportationSurfaceSplitter.DefaultSegmentLengthMeters;
    public const double MinTerrainAlignedTransportationSegmentLengthMeters = TerrainAlignedTransportationSurfaceSplitter.MinSegmentLengthMeters;
    public const double TerrainAlignedTransportationSegmentLengthByWidthRatio = TerrainAlignedTransportationSurfaceSplitter.SegmentLengthByWidthRatio;
    public static readonly MaterialDepthOffset DefaultTerrainAlignedMaterialDepthOffset = CityGmlSurfaceMaterialResolver.TerrainAlignedDepthOffset;

    private static readonly Quaternion GridMeshTerrainRotation = new(
        X: Math.Sqrt(0.5),
        Y: 0.0,
        Z: 0.0,
        W: Math.Sqrt(0.5));
    private static readonly XNamespace Gml = "http://www.opengis.net/gml";

    internal static TerrainTextureOverlay[] CreateDemTerrainTextureOverlays(
        MeshCodeBounds demBounds,
        IReadOnlyList<string> requestedMeshCodes)
    {
        return DemSourceDiscoverySupport.CreateDemTerrainOverlayRegions(
                DemTerrainBounds.FromProjectionModel(demBounds),
                requestedMeshCodes)
            .Select(static region => DemTerrainTextureDefaults.CreatePlateauOrthoWithGsiFallbackOverlay(region.GeographicBounds))
            .ToArray();
    }

    private static bool TryCreateMeshCodeBounds(string meshCode, out MeshCodeBounds? meshCodeArea)
    {
        meshCodeArea = MeshCodeBounds.TryParse(meshCode);
        return meshCodeArea is not null;
    }

    private static (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude) GetBounds(
        IEnumerable<ParsedCityObject> cityObjects)
    {
        List<GeodeticPoint> allPoints = cityObjects
            .SelectMany(static cityObject => cityObject.Surfaces)
            .SelectMany(static surface => surface.Vertices)
            .ToList();

        return (
            allPoints.Min(static point => point.Latitude),
            allPoints.Max(static point => point.Latitude),
            allPoints.Min(static point => point.Longitude),
            allPoints.Max(static point => point.Longitude),
            allPoints.Min(static point => point.Altitude));
    }

    private static List<ParsedSurface> SubdivideTransportationSurfaceForTerrainAlignment(
        ParsedSurface surface,
        ParsedCityObject cityObject)
    {
        if (surface.InteriorRings.Length != 0
            || surface.ExteriorRing.Vertices.Length != 4)
        {
            return [surface];
        }

        GeodeticPoint cityObjectOrigin = ResolveProjectionCityObjectOrigin(cityObject);
        LocalCartesian? cityObjectCartesian = cityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                cityObject.ReferenceSystem.Geocentric)
            : null;
        Float3[] positions = surface.ExteriorRing.Vertices
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        if (!IsNearHorizontalSurface(positions))
        {
            return [surface];
        }

        EdgePairSelection edgePair = RoadSurfaceEdgePairSelector.Select(
            global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.FromProjectionModel(surface.ExteriorRing),
            positions);
        List<global::PlateauResoniteLink.Application.Importing.ParsedSurface> strips =
            TerrainAlignedTransportationSurfaceSplitter.Split(
                global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.FromProjectionModel(surface),
                positions,
                edgePair);
        return strips.Select(global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.ToProjectionModel).ToList();
    }

    private static List<global::PlateauResoniteLink.Application.Importing.ParsedSurface> SubdivideTransportationSurfaceForTerrainAlignment(
        global::PlateauResoniteLink.Application.Importing.ParsedSurface surface,
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject)
    {
        if (surface.InteriorRings.Length != 0
            || surface.ExteriorRing.Vertices.Length != 4)
        {
            return [surface];
        }

        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin = ResolveParsedCityObjectOrigin(cityObject);
        LocalCartesian? cityObjectCartesian = cityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                cityObject.ReferenceSystem.Geocentric)
            : null;
        Float3[] positions = surface.ExteriorRing.Vertices
            .Select(point => CreateScenePosition(point.ToProjectionModel(), cityObjectOrigin.ToProjectionModel(), cityObjectCartesian))
            .ToArray();
        if (!IsNearHorizontalSurface(positions))
        {
            return [surface];
        }

        EdgePairSelection edgePair = RoadSurfaceEdgePairSelector.Select(surface.ExteriorRing, positions);
        return TerrainAlignedTransportationSurfaceSplitter.Split(surface, positions, edgePair);
    }

    private static bool IsTerrainDependentCityObject(ParsedCityObject cityObject)
    {
        return string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            || ShouldTerrainAlignCityObject(cityObject);
    }

    private static bool ShouldTerrainAlignCityObject(ParsedCityObject cityObject)
    {
        return CityGmlTerrainConformer.ShouldTerrainAlign(cityObject.PackageName, cityObject.LodLevel);
    }

    private static bool IsNearHorizontalSurface(Float3[] positions)
    {
        Float3? normal = ComputePolygonNormal(positions);
        return normal is not null && Math.Abs(normal.Y) >= 0.7;
    }

    private static GeodeticPoint ComputeGlobalOrigin(IEnumerable<ParsedCityObject> cityObjects)
    {
        return CreateGlobalOrigin(GetBounds(cityObjects), requestedMeshCodeBounds: null, isGeographicReferenceSystem: false);
    }

    private static GeodeticPoint CreateGlobalOrigin(
        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude) bounds,
        MeshCodeBounds? requestedMeshCodeBounds,
        bool isGeographicReferenceSystem)
    {
        if (isGeographicReferenceSystem && requestedMeshCodeBounds is not null)
        {
            return new GeodeticPoint(
                Latitude: (requestedMeshCodeBounds.SouthLatitude + requestedMeshCodeBounds.NorthLatitude) / 2.0,
                Longitude: (requestedMeshCodeBounds.WestLongitude + requestedMeshCodeBounds.EastLongitude) / 2.0,
                Altitude: bounds.minAltitude);
        }

        return new GeodeticPoint(
            Latitude: (bounds.minLatitude + bounds.maxLatitude) / 2.0,
            Longitude: (bounds.minLongitude + bounds.maxLongitude) / 2.0,
            Altitude: bounds.minAltitude);
    }

    private static CoordinateReferenceSystem GetReferenceSystem(IEnumerable<ParsedCityObject> cityObjects)
    {
        CoordinateReferenceSystem? referenceSystem = null;

        foreach (ParsedCityObject cityObject in cityObjects)
        {
            if (referenceSystem is null)
            {
                referenceSystem = cityObject.ReferenceSystem;
                continue;
            }

            if (!referenceSystem.IsCompatibleWith(cityObject.ReferenceSystem))
            {
                throw new PlateauImportValidationException(
                    [$"Mixed CityGML coordinate reference systems are not supported. Found '{referenceSystem.SrsName}' and '{cityObject.ReferenceSystem.SrsName}'."]);
            }
        }

        return referenceSystem
            ?? throw new PlateauImportValidationException(["No CityGML coordinate reference system was resolved."]);
    }

    internal static ImportedCityObject ProjectCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        return CityGmlTriangleMeshCityObjectProjection.Project(
            cityObject,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlay,
            materialResolver);
    }

    private static GeodeticPoint ResolveProjectionCityObjectOrigin(ParsedCityObject cityObject)
    {
        return CityObjectOriginResolver.Resolve(
            cityObject.GeodeticOriginOverride,
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices),
            static point => point.Latitude,
            static point => point.Longitude,
            static point => point.Altitude,
            static (latitude, longitude, altitude) => new GeodeticPoint(latitude, longitude, altitude));
    }

    private static global::PlateauResoniteLink.Application.Importing.GeodeticPoint ResolveParsedCityObjectOrigin(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject)
    {
        return CityObjectOriginResolver.Resolve(
            cityObject.GeodeticOriginOverride,
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices));
    }

    private static double ResolveParsedMinimumAltitude(
        IEnumerable<global::PlateauResoniteLink.Application.Importing.ParsedSurface> surfaces)
    {
        return CityObjectAltitudeMetricsResolver.GetMinimumAltitude(
            surfaces.SelectMany(static surface => surface.Vertices));
    }

    private static double? ResolveParsedGeometryHeightMeters(
        IEnumerable<global::PlateauResoniteLink.Application.Importing.ParsedSurface> surfaces)
    {
        return CityObjectAltitudeMetricsResolver.TryGetGeometryHeightMeters(
            surfaces.SelectMany(static surface => surface.Vertices));
    }

    private static DemUvProjection? TryCreateDemUvProjection(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        TerrainTextureOverlay? demTerrainTextureOverlay)
    {
        if (demTerrainTextureOverlay is null
            || TerrainOverlayMeshCodeResolver.ResolveMeshCode(cityObject.ActualMeshCode, demTerrainTextureOverlay) is not { } terrainMeshCode
            || !TryCreateMeshCodeBounds(terrainMeshCode, out MeshCodeBounds? meshCodeBounds))
        {
            return null;
        }

        return CreateDemUvProjection(
            meshCodeBounds!.WestLongitude,
            meshCodeBounds.EastLongitude,
            meshCodeBounds.NorthLatitude,
            meshCodeBounds.SouthLatitude);
    }

    private static DemUvProjection? TryCreateDemUvProjection(
        ParsedCityObject cityObject,
        TerrainTextureOverlay? demTerrainTextureOverlay)
    {
        return TryCreateDemUvProjection(cityObject.ActualMeshCode, demTerrainTextureOverlay);
    }

    private static DemUvProjection? TryCreateDemUvProjection(
        string actualMeshCode,
        TerrainTextureOverlay? demTerrainTextureOverlay)
    {
        if (demTerrainTextureOverlay is null
            || TerrainOverlayMeshCodeResolver.ResolveMeshCode(actualMeshCode, demTerrainTextureOverlay) is not { } terrainMeshCode
            || !TryCreateMeshCodeBounds(terrainMeshCode, out MeshCodeBounds? meshCodeBounds))
        {
            return null;
        }

        return CreateDemUvProjection(
            meshCodeBounds!.WestLongitude,
            meshCodeBounds.EastLongitude,
            meshCodeBounds.NorthLatitude,
            meshCodeBounds.SouthLatitude);
    }

    private static DemUvProjection CreateDemUvProjection(
        double westLongitude,
        double eastLongitude,
        double northLatitude,
        double southLatitude)
    {
        double west = WebMercatorTileMath.LongitudeToNormalizedX(westLongitude);
        double east = WebMercatorTileMath.LongitudeToNormalizedX(eastLongitude);
        double north = WebMercatorTileMath.LatitudeToNormalizedY(northLatitude);
        double south = WebMercatorTileMath.LatitudeToNormalizedY(southLatitude);
        double width = Math.Max(east - west, 1e-12);
        double height = Math.Max(south - north, 1e-12);

        return new DemUvProjection(west, south, width, height);
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

    private static string CreateTerrainOverlayToken(TerrainTextureOverlay terrainOverlay)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"terrain-overlay-{terrainOverlay.PackageName.ToLowerInvariant()}-{terrainOverlay.SourceDescriptorKey}-bounds-{FormatBounds(terrainOverlay.GeographicBounds)}");
    }

    private static string MaterialTypeToken(MaterialType materialType) =>
        materialType.ToString().ToLowerInvariant();

    private static string ProjectionToken(MaterialProjection projection)
    {
        return projection switch
        {
            MaterialProjection.Uv => "uv",
            MaterialProjection.Triplanar => "triplanar",
            _ => projection.ToString().ToLowerInvariant(),
        };
    }

    private static string FormatFloat2(Float2? value)
    {
        return value is null
            ? "none"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{FormatRounded(value.X)}-{FormatRounded(value.Y)}");
    }

    private static string FormatDepth(MaterialDepthOffset? value)
    {
        return value is null
            ? "none"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{FormatRounded(value.Factor)}-{FormatRounded(value.Units)}");
    }

    private static string FormatColor(ColorRgba value) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{FormatRounded(value.R)}-{FormatRounded(value.G)}-{FormatRounded(value.B)}-{FormatRounded(value.A)}");

    private static string FormatBounds(GeographicRectangle bounds) =>
        TerrainOverlayDiagnostics.FormatBounds(bounds);

    private static string FormatRounded(double value) =>
        TerrainOverlayDiagnostics.FormatRounded(value);

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static bool AreSamePoint(GeodeticPoint left, GeodeticPoint right)
    {
        return Math.Abs(left.Latitude - right.Latitude) < 1e-8
            && Math.Abs(left.Longitude - right.Longitude) < 1e-8
            && Math.Abs(left.Altitude - right.Altitude) < 1e-8;
    }

    internal static IEnumerable<ImportedCityObject> ProjectCityObjects(
        global::PlateauResoniteLink.Application.Importing.CachedSourceFileDescriptor sourceFile,
        global::PlateauResoniteLink.Application.Importing.CoordinateReferenceSystem referenceSystem,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver,
        Func<global::PlateauResoniteLink.Application.Importing.ParsedCityObject, bool>? predicate = null,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return CityGmlParsedCityObjectProjection.ProjectSourceFile(
            sourceFile,
            referenceSystem,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlays,
            requestedMeshCodeBounds,
            request,
            materialResolver,
            predicate,
            progressReporter,
            cancellationToken);
    }

    internal static IEnumerable<ImportedCityObject> ProjectParsedCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject parsedCityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        ProjectionTerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return CityGmlParsedCityObjectProjection.Project(
            parsedCityObject,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlays,
            requestedMeshCodeBounds,
            terrainHeightSampler,
            request,
            materialResolver,
            progressReporter,
            cancellationToken);
    }

    internal static IEnumerable<MaterialBinding> EnumerateCommonMaterialsForParsedCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject parsedCityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds,
        ProjectionTerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver)
    {
        return CityGmlParsedCityObjectProjection.EnumerateCommonMaterials(
            parsedCityObject,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlays,
            requestedMeshCodeBounds,
            terrainHeightSampler,
            request,
            materialResolver);
    }

    private static MaterialBinding[] CreateCommonMaterialBindings(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        return CityGmlSurfaceMaterialResolver.CreateSharedCommonMaterialBindings(
            global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.FromProjectionModel(cityObject),
            global::PlateauResoniteLink.Application.Importing.GeodeticPoint.FromProjectionModel(cityObjectOrigin),
            cityObjectCartesian,
            demTerrainTextureOverlay,
            materialResolver);
    }

    private static bool TryProjectDemTerrainGridCityObject(
        ParsedCityObject cityObject,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        PlateauImportRequest request,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds,
        IDefaultMaterialResolver materialResolver,
        out ImportedCityObject? heightMapCityObject)
    {
        heightMapCityObject = null;

        GeodeticPoint cityObjectOrigin = ResolveProjectionCityObjectOrigin(cityObject);
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

        DemTerrainGridHeightSamples heightSamples = CityGmlDemTerrainGridSampler.Sample(
            minX,
            maxX,
            minZ,
            maxZ,
            request.TerrainGridMetersPerVertex,
            request.TerrainGridMaxResolution,
            seaLevelLocalHeight,
            triangles);
        int width = heightSamples.Width;
        int height = heightSamples.Height;
        double[] localHeights = heightSamples.LocalHeights;
        double minHeight = localHeights.Min();
        double maxHeight = localHeights.Max();

        MaterialBinding[] materials = CreateDemTerrainGridMaterials(
            cityObject,
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
            // Elements.Assets.Grid centers vertices in-plane, so split DEM chunks need their own bbox-center offset here.
            X = slotPosition.X + centerX,
            // GridMesh displaces the inverted terrain grid downward in world Y, so the slot must start at the patch-local maximum height.
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
            Materials: materials,
            SourceFileRelativePath: cityObject.SourceFileRelativePath);
        return true;
    }

    private static bool TryProjectDemTerrainGridCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        PlateauImportRequest request,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        IDefaultMaterialResolver materialResolver,
        Action<string>? progressReporter,
        CancellationToken cancellationToken,
        out ImportedCityObject? heightMapCityObject)
    {
        return CityGmlDemTerrainGridCityObjectProjection.TryProject(
            cityObject,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlay,
            request,
            requestedMeshCodeBounds,
            materialResolver,
            progressReporter,
            cancellationToken,
            out heightMapCityObject);
    }

    private static MaterialBinding[] CreateDemTerrainGridMaterials(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        string requestedMeshCode,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds,
        IDefaultMaterialResolver materialResolver)
    {
        return CityGmlSurfaceMaterialResolver.CreateDemTerrainGridMaterials(
            global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.FromProjectionModel(cityObject),
            global::PlateauResoniteLink.Application.Importing.GeodeticPoint.FromProjectionModel(cityObjectOrigin),
            cityObjectCartesian,
            demTerrainTextureOverlay,
            requestedMeshCode,
            requestedMeshCodeBounds,
            materialResolver);
    }

    private static Float2 ToContractFloat2(ScalarPair value) => new(value.X, value.Y);

    private static ColorRgba ToInternalColor(ColorRgba value) => new(value.R, value.G, value.B, value.A);

    private static Float3 ToContractFloat3(Float3 value) => new(value.X, value.Y, value.Z);

    private static Quaternion ToContractQuaternion(Quaternion value) => new(value.X, value.Y, value.Z, value.W);

    private static TextureUvRect? TryCreateDemTerrainGridOccupiedUvRect(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        if (demTerrainTextureOverlay is null)
        {
            return null;
        }

        GeographicRectangle? demObjectBounds = TryGetDemObjectGeographicBounds(cityObject, demTerrainTextureOverlay);
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject parsedCityObject =
            global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.FromProjectionModel(cityObject);
        ResolvedSurfaceMaterial? representativeSurface = CityGmlSurfaceMaterialResolver.ResolveSurfaces(
                parsedCityObject,
                global::PlateauResoniteLink.Application.Importing.GeodeticPoint.FromProjectionModel(cityObjectOrigin),
                cityObjectCartesian,
                demTerrainTextureOverlay,
                materialResolver)
            .FirstOrDefault(static resolvedSurface => resolvedSurface.Surface.UsesGeneratedDemTexture);
        if (representativeSurface is null)
        {
            return null;
        }

        TextureUvRect? occupiedUvRect = DemTerrainOverlayAssignment.TryCreateTerrainGridOccupiedUvRect(
            parsedCityObject,
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

        return ResolveProjectionCityObjectGeographicBounds(cityObject);
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
            ResolveProjectionCityObjectGeographicBounds(cityObject),
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

        if ((clippedMaxX - clippedMinX) <= 1e-6 || (clippedMaxZ - clippedMinZ) <= 1e-6)
        {
            return new DemTerrainGridBounds(rawMinX, rawMaxX, rawMinZ, rawMaxZ);
        }

        return new DemTerrainGridBounds(clippedMinX, clippedMaxX, clippedMinZ, clippedMaxZ);
    }

    private static GeographicRectangle ResolveProjectionCityObjectGeographicBounds(ParsedCityObject cityObject)
    {
        return CityObjectGeographicBoundsResolver.Resolve(
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices),
            static point => point.Latitude,
            static point => point.Longitude);
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

    private static TriangleMeshGeometry AssertTriangleMeshGeometry(ImportedCityObject cityObject)
    {
        return cityObject.Geometry as TriangleMeshGeometry
            ?? throw new InvalidOperationException("Dynamic terrain static variant must be projected as a triangle mesh.");
    }

    private static TerrainGridGeometry AssertTerrainGridGeometry(ImportedCityObject cityObject)
    {
        return cityObject.Geometry as TerrainGridGeometry
            ?? throw new InvalidOperationException("Dynamic terrain grid variant must be projected as a terrain grid.");
    }

    private static bool HasRenderableGeometry(ImportedCityObject cityObject)
    {
        return cityObject.Geometry switch
        {
            TriangleMeshGeometry triangleMesh => triangleMesh.Mesh.Submeshes.Count > 0,
            TerrainGridGeometry heightMap => heightMap.Width > 1 && heightMap.Height > 1,
            DynamicTerrainGeometry dynamicTerrain =>
                dynamicTerrain.StaticMesh.Mesh.Submeshes.Count > 0
                && dynamicTerrain.GridMesh.Width > 1
                && dynamicTerrain.GridMesh.Height > 1,
            _ => false,
        };
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

    internal static void ValidateCompatibleReferenceSystem(
        CoordinateReferenceSystem expectedReferenceSystem,
        CoordinateReferenceSystem? actualReferenceSystem)
    {
        if (actualReferenceSystem is null || expectedReferenceSystem.IsCompatibleWith(actualReferenceSystem))
        {
            return;
        }

        throw new PlateauImportValidationException(
            [$"Mixed CityGML coordinate reference systems are not supported. Found '{expectedReferenceSystem.SrsName}' and '{actualReferenceSystem.SrsName}'."]);
    }

    internal sealed record ParsedCityObject(
        string SlotKey,
        string DisplayName,
        string PackageName,
        string ActualMeshCode,
        int? LodLevel,
        ParsedSurface[] Surfaces,
        CoordinateReferenceSystem ReferenceSystem,
        string SourceFileRelativePath,
        bool SharedAcrossMeshCodes,
        bool TerrainAligned = false,
        GeodeticPoint? GeodeticOriginOverride = null,
        int? FloorsAboveGround = null,
        double? MeasuredHeightMeters = null,
        BuildingAttributeContext? BuildingAttributes = null,
        double? GeometryHeightMeters = null);

    internal sealed record ParsedRing(
        string RingId,
        GeodeticPoint[] Vertices,
        IReadOnlyList<Float2>? UVs);

    private sealed record DemTerrainGridBounds(
        double MinX,
        double MaxX,
        double MinZ,
        double MaxZ);

    internal sealed record ParsedSurface(
        string PolygonId,
        ParsedSurfaceSemantic Semantic,
        ParsedRing ExteriorRing,
        ParsedRing[] InteriorRings,
        ColorRgba BaseColor,
        TexturePayload? TexturePayload,
        bool UsesGeneratedDemTexture = false,
        MaterialOpticalProperties? OpticalProperties = null)
    {
        public IEnumerable<GeodeticPoint> Vertices =>
            ExteriorRing.Vertices.Concat(InteriorRings.SelectMany(static ring => ring.Vertices));
    }

    internal enum ParsedSurfaceSemantic
    {
        Unknown = 0,
        Wall = 1,
        Roof = 2,
        Ground = 3,
        Closure = 4,
        OuterCeiling = 5,
        OuterFloor = 6,
    }

    internal sealed record GeodeticPoint(
        double Latitude,
        double Longitude,
        double Altitude);

    internal sealed record CoordinateReferenceSystem(
        string SrsName,
        Geocentric? Geocentric,
        string CompatibilityKey)
    {
        public bool IsGeographic => Geocentric is not null;

        public bool IsCompatibleWith(CoordinateReferenceSystem other)
        {
            ArgumentNullException.ThrowIfNull(other);

            return string.Equals(CompatibilityKey, other.CompatibilityKey, StringComparison.Ordinal);
        }

        public static CoordinateReferenceSystem Parse(XDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            string? srsName = document
                .Descendants(Gml + "Envelope")
                .Attributes("srsName")
                .Select(static attribute => attribute.Value.Trim())
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

            return Parse(srsName);
        }

        public static CoordinateReferenceSystem Parse(string? srsName)
        {
            if (string.IsNullOrWhiteSpace(srsName))
            {
                return new CoordinateReferenceSystem("local-cartesian", null, "local-cartesian");
            }

            (Geocentric geocentric, string compatibilityKey) = ResolveGeocentric(srsName);
            return new CoordinateReferenceSystem(srsName, geocentric, compatibilityKey);
        }

        private static (Geocentric Geocentric, string CompatibilityKey) ResolveGeocentric(string srsName)
        {
            if (srsName.EndsWith("/6697", StringComparison.Ordinal)
                || srsName.EndsWith("EPSG:6697", StringComparison.OrdinalIgnoreCase)
                || srsName.EndsWith("/6668", StringComparison.Ordinal)
                || srsName.EndsWith("EPSG:6668", StringComparison.OrdinalIgnoreCase))
            {
                return (new Geocentric(Ellipsoid.GRS80), "jgd2011");
            }

            if (srsName.EndsWith("/6696", StringComparison.Ordinal)
                || srsName.EndsWith("EPSG:6696", StringComparison.OrdinalIgnoreCase))
            {
                return (new Geocentric(Ellipsoid.GRS80), "jgd2011");
            }

            if (srsName.EndsWith("/4979", StringComparison.Ordinal)
                || srsName.EndsWith("EPSG:4979", StringComparison.OrdinalIgnoreCase)
                || srsName.EndsWith("/4326", StringComparison.Ordinal)
                || srsName.EndsWith("EPSG:4326", StringComparison.OrdinalIgnoreCase))
            {
                return (Geocentric.WGS84, "wgs84");
            }

            throw new PlateauImportValidationException(
                [$"Unsupported CityGML CRS '{srsName}'. Only geographic 3D CRS values currently used by PLATEAU are supported."]);
        }
    }

}
