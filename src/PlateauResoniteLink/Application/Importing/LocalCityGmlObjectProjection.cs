using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using GeographicLib;

using LibTessDotNet;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;

using Geocentric = GeographicLib.Geocentric;
using LocalCartesian = GeographicLib.LocalCartesian;

namespace PlateauResoniteLink.Application.Importing;

internal static class LocalCityGmlObjectProjection
{
    private const double BuildingBottomCullBandMeters = 0.1;
    private const double UnknownRoofBottomAltitudeToleranceMeters = 0.1;
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
    private static readonly ColorRgba DefaultMaterialColor = new(1.0, 1.0, 1.0, 1.0);
    private static readonly ColorRgba DefaultVegetationMaterialColor = new(0.32, 0.58, 0.24, 1.0);
    private static readonly XNamespace App = "http://www.opengis.net/citygml/appearance/2.0";
    private static readonly XNamespace Core = "http://www.opengis.net/citygml/2.0";
    private static readonly XNamespace Gml = "http://www.opengis.net/gml";
    internal static ParsedCityObject? ParseCityObject(
        XElement cityObjectElement,
        string packageName,
        string relativeSourceFile,
        string actualMeshCode,
        bool sharedAcrossMeshCodes,
        ICityGmlAppearanceStore appearanceStore,
        ICityGmlLodSelector lodSelector,
        CoordinateReferenceSystem coordinateReferenceSystem,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds,
        LodFilteringStrategy lodFilteringStrategy)
    {
        string objectTypeName = cityObjectElement.Name.LocalName;
        string objectId = GetAttribute(cityObjectElement, Gml + "id") ?? objectTypeName;
        string? displayName = cityObjectElement.Elements(Gml + "name").FirstOrDefault()?.Value.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = objectId;
        }

        string resolvedActualMeshCode = CityGmlMeshCodeBoundsFilter.ResolveActualMeshCode(
            packageName,
            displayName!,
            objectId,
            actualMeshCode,
            sharedAcrossMeshCodes);
        BuildingAttributeContext buildingAttributes = BuildingAttributeParser.Parse(cityObjectElement);
        int? floorsAboveGround = BuildingAttributeQueries.TryGetKnownPositiveInteger(buildingAttributes.StoreysAboveGround);
        double? measuredHeightMeters = BuildingAttributeQueries.TryGetKnownPositiveMetric(buildingAttributes.MeasuredHeightMeters);

        bool isMarking = displayName.Contains("Marking", StringComparison.OrdinalIgnoreCase)
            || objectId.Contains("Marking", StringComparison.OrdinalIgnoreCase)
            || objectId.Contains("_road_marking", StringComparison.Ordinal);

        CityGmlLodSelection lodSelection = lodSelector.SelectPreferredSurfaceElements(
            cityObjectElement,
            packageName,
            isMarking,
            lodFilteringStrategy);
        XElement[] preferredSurfaceElements = lodSelection.SurfaceElements;
        int? lodLevel = lodSelection.LodLevel;

        if (!lodFilteringStrategy.ShouldIncludeByPattern(packageName, objectId, isMarking))
        {
            return null;
        }

        if (preferredSurfaceElements.Length == 0 && lodFilteringStrategy.ShouldExcludeLod(packageName, lodLevel, isMarking))
        {
            return null;
        }

        ParsedSurface[] surfaces = preferredSurfaceElements
            .Select(surfaceElement => CityGmlParsedSurfaceReader.Parse(surfaceElement, appearanceStore))
            .Where(static surface => surface is not null)
            .Select(static surface => surface!)
            .Select(surface => CityGmlParsedSurfaceReader.ApplyPackageDefaults(packageName, surface))
            .OrderBy(static surface => ParsedSurfaceStableSortKey.Create(surface), StringComparer.Ordinal)
            .ToArray();

        if (surfaces.Length == 0)
        {
            return null;
        }

        if (!CityGmlMeshCodeBoundsFilter.IntersectsRequestedMeshCodeBounds(
                resolvedActualMeshCode,
                sharedAcrossMeshCodes,
                coordinateReferenceSystem,
                requestedMeshCodeBounds,
                surfaces))
        {
            return null;
        }

        string fileStem = Path.GetFileNameWithoutExtension(relativeSourceFile);
        string slotKey = SanitizeIdentifier($"{packageName}_{fileStem}_{objectId}");
        return new ParsedCityObject(
            slotKey,
            displayName!,
            packageName,
            resolvedActualMeshCode,
            lodLevel,
            surfaces,
            coordinateReferenceSystem,
            relativeSourceFile,
            SharedAcrossMeshCodes: sharedAcrossMeshCodes,
            FloorsAboveGround: floorsAboveGround,
            MeasuredHeightMeters: measuredHeightMeters,
            BuildingAttributes: buildingAttributes);
    }

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

    private static (XElement[] SurfaceElements, int? LodLevel) SelectPreferredLodSurfaceElements(
        XElement cityObjectElement,
        string packageName,
        bool isMarking,
        LodFilteringStrategy lodFilteringStrategy)
    {
        (XElement SurfaceElement, int? LodLevel)[] surfaces = cityObjectElement
            .Descendants()
            .Where(element => element.Name == Gml + "Polygon" || element.Name == Gml + "Triangle")
            .Select(surfaceElement => (surfaceElement, GetSurfaceLodLevel(surfaceElement, cityObjectElement)))
            .ToArray();

        int[] explicitLodLevels = surfaces
            .Where(static surface => surface.LodLevel.HasValue)
            .Select(static surface => surface.LodLevel!.Value)
            .ToArray();

        // Filter out excluded LODs before finding the highest
        int[] validLodLevels = explicitLodLevels
            .Where(lod => !lodFilteringStrategy.ShouldExcludeLod(packageName, lod, isMarking))
            .ToArray();

        int? highestLod = validLodLevels.Length > 0
            ? validLodLevels.Max()
            : null;

        XElement[] selectedSurfaces = highestLod.HasValue
            ? surfaces
                .Where(surface => surface.LodLevel == highestLod.Value)
                .Select(static surface => surface.SurfaceElement)
                .ToArray()
            : surfaces
                .Where(surface => !lodFilteringStrategy.ShouldExcludeLod(packageName, surface.LodLevel, isMarking))
                .Select(static surface => surface.SurfaceElement)
                .ToArray();

        return (selectedSurfaces, highestLod);
    }

    private static int? GetSurfaceLodLevel(XElement surfaceElement, XElement cityObjectElement)
    {
        for (XElement? ancestor = surfaceElement.Parent; ancestor is not null && ancestor != cityObjectElement; ancestor = ancestor.Parent)
        {
            if (TryParseLodLevel(ancestor.Name.LocalName, out int lodLevel))
            {
                return lodLevel;
            }
        }

        return null;
    }

    private static bool TryParseLodLevel(string localName, out int lodLevel)
    {
        lodLevel = 0;
        if (!localName.StartsWith("lod", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int digitStart = 3;
        int digitLength = 0;
        while (digitStart + digitLength < localName.Length
            && char.IsDigit(localName[digitStart + digitLength]))
        {
            digitLength++;
        }

        return digitLength > 0
            && int.TryParse(
                localName.AsSpan(digitStart, digitLength),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out lodLevel);
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

    internal static MeshCodeBounds? ResolveDemTerrainBounds(
        IEnumerable<ParsedSourceFileResult> demParsedSourceFiles,
        MeshCodeBounds? fallbackBounds)
    {
        DemTerrainBounds? bounds = DemSourceDiscoverySupport.ResolveDemTerrainBounds(
            demParsedSourceFiles.Select(global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.FromProjectionModel),
            fallbackBounds is null ? null : DemTerrainBounds.FromProjectionModel(fallbackBounds));
        return bounds?.ToProjectionModel();
    }

    private static ProjectionTerrainHeightTriangle[] ExtractTerrainHeightTriangles(
        IEnumerable<ParsedCityObject> cityObjects)
    {
        return DemSourceDiscoverySupport.CreateTerrainHeightTriangles(
                cityObjects.Select(global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.FromProjectionModel))
            .Select(static triangle => triangle.ToProjectionModel())
            .ToArray();
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

        EdgePairSelection edgePair = RoadSurfaceEdgePairSelector.Select(surface.ExteriorRing, positions);
        return TerrainAlignedTransportationSurfaceSplitter.Split(surface, positions, edgePair);
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

        EdgePairSelection edgePair = RoadSurfaceEdgePairSelector.Select(
            global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.ToProjectionModel(surface.ExteriorRing),
            positions);
        ParsedSurface projectionSurface = global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.ToProjectionModel(surface);
        List<ParsedSurface> strips = TerrainAlignedTransportationSurfaceSplitter.Split(
            projectionSurface,
            positions,
            edgePair);
        if (strips.Count == 1 && ReferenceEquals(strips[0], projectionSurface))
        {
            return [surface];
        }

        return strips.Select(global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.FromProjectionModel).ToList();
    }

    private static Float3 NormalizeHorizontal(Float3 value)
    {
        double length = Math.Sqrt((value.X * value.X) + (value.Z * value.Z));
        if (length <= 1e-8)
        {
            return new Float3(0.0, 0.0, 0.0);
        }

        return new Float3(value.X / length, 0.0, value.Z / length);
    }

    private static double DotHorizontal(Float3 left, Float3 right)
    {
        return (left.X * right.X) + (left.Z * right.Z);
    }

    private static double LengthSquared(Float3 value)
    {
        return (value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z);
    }

    private static ParsedSurface[] ConformSurfacesToTerrain(
        string packageName,
        ParsedSurface[] surfaces,
        ProjectionTerrainHeightSampler terrainHeightSampler,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        ref bool terrainAligned)
    {
        ParsedSurface[] conformedSurfaces = new ParsedSurface[surfaces.Length];
        for (int index = 0; index < surfaces.Length; index++)
        {
            ParsedSurface surface = surfaces[index];
            conformedSurfaces[index] = ShouldConformSurfaceToTerrain(
                    packageName,
                    surface,
                    cityObjectOrigin,
                    cityObjectCartesian)
                ? ConformSurfaceToTerrain(surface, terrainHeightSampler, ref terrainAligned)
                : surface;
        }

        return conformedSurfaces;
    }

    private static ParsedSurface[] ConformRoadSurfacesToTerrainWithFallback(
        ParsedSurface[] surfaces,
        ProjectionTerrainHeightSampler terrainHeightSampler,
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
            conformedSurfaces[index] = ConformRoadSurfaceToTerrainWithFallback(
                surfaces[index],
                terrainHeightSampler,
                anchors,
                ref terrainAligned);
        }

        return conformedSurfaces;
    }

    private static ParsedSurface ConformRoadSurfaceToTerrainWithFallback(
        ParsedSurface surface,
        ProjectionTerrainHeightSampler terrainHeightSampler,
        IReadOnlyList<TerrainSampleAnchor> anchors,
        ref bool terrainAligned)
    {
        ParsedRing exteriorRing = ConformRoadRingToTerrainWithFallback(surface.ExteriorRing, terrainHeightSampler, anchors, ref terrainAligned);
        ParsedRing[] interiorRings = new ParsedRing[surface.InteriorRings.Length];
        for (int index = 0; index < surface.InteriorRings.Length; index++)
        {
            interiorRings[index] = ConformRoadRingToTerrainWithFallback(surface.InteriorRings[index], terrainHeightSampler, anchors, ref terrainAligned);
        }

        return surface with
        {
            ExteriorRing = exteriorRing,
            InteriorRings = interiorRings,
        };
    }

    private static ParsedRing ConformRoadRingToTerrainWithFallback(
        ParsedRing ring,
        ProjectionTerrainHeightSampler terrainHeightSampler,
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

    private static ParsedSurface ConformSurfaceToTerrain(
        ParsedSurface surface,
        ProjectionTerrainHeightSampler terrainHeightSampler,
        ref bool terrainAligned)
    {
        ParsedRing exteriorRing = ConformRingToTerrain(surface.ExteriorRing, terrainHeightSampler, ref terrainAligned);
        ParsedRing[] interiorRings = new ParsedRing[surface.InteriorRings.Length];
        for (int index = 0; index < surface.InteriorRings.Length; index++)
        {
            interiorRings[index] = ConformRingToTerrain(surface.InteriorRings[index], terrainHeightSampler, ref terrainAligned);
        }

        return surface with
        {
            ExteriorRing = exteriorRing,
            InteriorRings = interiorRings,
        };
    }

    private static ParsedRing ConformRingToTerrain(
        ParsedRing ring,
        ProjectionTerrainHeightSampler terrainHeightSampler,
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
            InterpolateUnsampledTerrainVertices(vertices, sampled, ref terrainAligned);
        }

        return ring with
        {
            Vertices = vertices,
        };
    }

    private static void InterpolateUnsampledTerrainVertices(
        GeodeticPoint[] vertices,
        bool[] sampled,
        ref bool terrainAligned)
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

    private static bool IsTerrainDependentCityObject(ParsedCityObject cityObject)
    {
        return string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            || ShouldTerrainAlignCityObject(cityObject);
    }

    private static bool ShouldTerrainAlignCityObject(ParsedCityObject cityObject)
    {
        return ShouldTerrainAlignCityObject(cityObject.PackageName, cityObject.LodLevel);
    }

    private static bool ShouldConformSurfaceToTerrain(
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
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        return IsNearHorizontalSurface(positions);
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
        double? geometryHeightMeters = cityObject.GeometryHeightMeters
            ?? ResolveParsedGeometryHeightMeters(cityObject.Surfaces);
        cityObject = ApplyGeneratedLod1Roof(cityObject) with
        {
            GeometryHeightMeters = geometryHeightMeters,
        };
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin = ResolveParsedCityObjectOrigin(cityObject);

        LocalCartesian? cityObjectCartesian = cityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                cityObject.ReferenceSystem.Geocentric)
            : null;
        Float3 slotPosition = CreateScenePosition(
            cityObjectOrigin.ToProjectionModel(),
            globalOriginPoint.ToProjectionModel(),
            globalCartesian);
        List<MeshVertex> vertices = [];
        List<MeshSubmesh> submeshes = [];
        List<MaterialBinding> materials = [];
        DemUvProjection? demUvProjection = TryCreateDemUvProjection(cityObject.ActualMeshCode, demTerrainTextureOverlay);

        List<ResolvedSurfaceMaterial> resolvedSurfaces =
        [
            .. CityGmlSurfaceMaterialResolver.ResolveSurfaces(
                cityObject,
                cityObjectOrigin,
                cityObjectCartesian,
                demTerrainTextureOverlay,
                materialResolver),
        ];

        IGrouping<MaterialGroupingKey, ResolvedSurfaceMaterial>[] materialGroups = resolvedSurfaces
            .GroupBy(
                resolvedSurface => MaterialGroupingPolicy.CreateKey(
                    cityObject.ActualMeshCode,
                    resolvedSurface.Material,
                    resolvedSurface.DepthOffset,
                    resolvedSurface.Material.TextureScale,
                    resolvedSurface.Surface.BaseColor,
                    resolvedSurface.Material.TextureOffset))
            .OrderBy(static group => group.Min(static surface => ParsedSurfaceStableSortKey.Create(surface.Surface)), StringComparer.Ordinal)
            .ToArray();

        for (int materialIndex = 0; materialIndex < materialGroups.Length; materialIndex++)
        {
            IGrouping<MaterialGroupingKey, ResolvedSurfaceMaterial> materialGroup = materialGroups[materialIndex];
            List<int> indices = [];
            FacadeUvProjectionContext? facadeUvProjectionContext = TryCreateFacadeUvProjectionContext(
                cityObject.PackageName,
                cityObject.Surfaces.Select(global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.ToProjectionModel),
                cityObjectOrigin.ToProjectionModel(),
                cityObjectCartesian);

            foreach (ResolvedSurfaceMaterial resolvedSurface in materialGroup
                         .OrderBy(static surface => ParsedSurfaceStableSortKey.Create(surface.Surface), StringComparer.Ordinal))
            {
                TriangulateSurface(
                    cityObject.PackageName,
                    resolvedSurface.Surface,
                    resolvedSurface.Material,
                    cityObjectOrigin.ToProjectionModel(),
                    cityObjectCartesian,
                    globalOriginPoint.ToProjectionModel(),
                    globalCartesian,
                    facadeUvProjectionContext,
                    demTerrainTextureOverlay,
                    demUvProjection,
                    vertices,
                    indices);
            }

            if (indices.Count == 0)
            {
                continue;
            }

            ResolvedSurfaceMaterial representativeSurface = materialGroup.First();
            submeshes.Add(new MeshSubmesh(materialIndex, indices));
            materials.Add(CityGmlSurfaceMaterialResolver.CreateMaterialBinding(cityObject.ActualMeshCode, representativeSurface, materialIndex));
        }

        return new ImportedCityObject(
            ObjectKey: cityObject.SlotKey,
            DisplayName: cityObject.DisplayName,
            PackageName: cityObject.PackageName,
            ActualMeshCode: cityObject.ActualMeshCode,
            LodLevel: cityObject.LodLevel,
            Transform: new Transform3D(ToContractFloat3(slotPosition)),
            Mesh: new ImportedMesh(vertices.ToArray(), submeshes.ToArray()),
            Materials: materials,
            SourceFileRelativePath: cityObject.SourceFileRelativePath);
    }

    private static global::PlateauResoniteLink.Application.Importing.ParsedCityObject ApplyGeneratedLod1Roof(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject)
    {
        if (!IsBuildingPackage(cityObject.PackageName)
            || cityObject.LodLevel != 1
            || !cityObject.ReferenceSystem.IsGeographic
            || cityObject.Surfaces.Any(static surface => IsGeneratedLod1RoofSurface(
                global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.ToProjectionModel(surface))))
        {
            return cityObject;
        }

        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin = ResolveParsedCityObjectOrigin(cityObject);
        LocalCartesian cityObjectCartesian = new(
            cityObjectOrigin.Latitude,
            cityObjectOrigin.Longitude,
            cityObjectOrigin.Altitude,
            cityObject.ReferenceSystem.Geocentric);
        if (!TryCreateLod1RoofFootprint(cityObject, cityObjectOrigin, cityObjectCartesian, out Lod1RoofFootprint? footprint))
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

        global::PlateauResoniteLink.Application.Importing.ParsedSurface[] generatedSurfaces =
            GeneratedLod1RoofSurfaceFactory.Create(resolvedFootprint, roofShape);
        if (generatedSurfaces.Length == 0)
        {
            return cityObject;
        }

        global::PlateauResoniteLink.Application.Importing.ParsedSurface[] surfaces =
        [
            .. cityObject.Surfaces.Where(surface => !string.Equals(surface.PolygonId, resolvedFootprint.TopSurface.PolygonId, StringComparison.Ordinal)),
            .. generatedSurfaces,
        ];
        return cityObject with { Surfaces = surfaces };
    }

    private static bool TryCreateLod1RoofFootprint(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin,
        LocalCartesian cityObjectCartesian,
        out Lod1RoofFootprint? footprint)
    {
        footprint = null;
        SurfaceProjectionInfo[] surfaceInfos = cityObject.Surfaces
            .Select(surface => CreateSurfaceProjectionInfo(
                global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.ToProjectionModel(surface),
                cityObjectOrigin.ToProjectionModel(),
                cityObjectCartesian))
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

        ParsedSurface topProjectionSurface = topCandidates[0].Surface;
        global::PlateauResoniteLink.Application.Importing.ParsedSurface? topSurface = cityObject.Surfaces
            .FirstOrDefault(surface => string.Equals(surface.PolygonId, topProjectionSurface.PolygonId, StringComparison.Ordinal));
        if (topSurface is null
            || topSurface.TexturePayload is not null
            || topSurface.InteriorRings.Length != 0)
        {
            return false;
        }

        global::PlateauResoniteLink.Application.Importing.GeodeticPoint[] ring = RemoveClosingPoint(topSurface.ExteriorRing.Vertices);
        if (ring.Length != 4)
        {
            return false;
        }

        Float3[] positions = ring
            .Select(point => CreateScenePosition(point.ToProjectionModel(), cityObjectOrigin.ToProjectionModel(), cityObjectCartesian))
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

    private static global::PlateauResoniteLink.Application.Importing.GeodeticPoint[] RemoveClosingPoint(
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint[] vertices)
    {
        if (vertices.Length > 1 && AreSamePoint(vertices[0].ToProjectionModel(), vertices[^1].ToProjectionModel()))
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

    private static double ResolveProjectionMinimumAltitude(IEnumerable<ParsedSurface> surfaces)
    {
        return CityObjectAltitudeMetricsResolver.GetMinimumAltitude(
            surfaces.SelectMany(static surface => surface.Vertices),
            static point => point.Altitude);
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

    private static ResolvedSurfaceMaterial ResolveSurfaceMaterial(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        ParsedSurface surface,
        double cityObjectMinAltitude,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        if (surface.UsesGeneratedDemTexture)
        {
            return new ResolvedSurfaceMaterial(
                surface,
                new ResolvedMaterial(
                    MaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind.Dataset,
                    MaterialProjection.Uv,
                    Family: null,
                    TextureScale: null,
                    ReuseScope: MaterialReuseScope.PerObject,
                    TerrainOverlay: demTerrainTextureOverlay),
                DepthOffset: null);
        }

        ResolvedMaterial? roofTerrainTextureMaterial = TryCreateRoofTerrainTextureMaterial(
            cityObject.ActualMeshCode,
            cityObject.PackageName,
            surface,
            cityObjectMinAltitude,
            demTerrainTextureOverlay,
            cityObjectOrigin,
            cityObjectCartesian);
        if (roofTerrainTextureMaterial is not null)
        {
            return new ResolvedSurfaceMaterial(
                surface with { BaseColor = DefaultMaterialColor },
                roofTerrainTextureMaterial,
                DepthOffset: null);
        }

        if (string.Equals(cityObject.PackageName, "veg", StringComparison.OrdinalIgnoreCase)
            && surface.TexturePayload is null)
        {
            if (HasExplicitMaterialColor(surface.BaseColor))
            {
                return new ResolvedSurfaceMaterial(
                    surface,
                    new ResolvedMaterial(
                        MaterialType.VertexColor,
                        TexturePayload: null,
                        TextureSourceKind.Bundled,
                        MaterialProjection.Uv,
                        Family: null,
                        TextureScale: null,
                        ReuseScope: MaterialReuseScope.PerObject),
                    DepthOffset: null);
            }

            return new ResolvedSurfaceMaterial(
                surface with { BaseColor = DefaultVegetationMaterialColor },
                new ResolvedMaterial(
                    MaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind.Bundled,
                    MaterialProjection.Uv,
                    Family: null,
                    TextureScale: null,
                    ReuseScope: MaterialReuseScope.PerObject),
                DepthOffset: null);
        }

        if (IsGeneratedRoadMarkingSurface(surface))
        {
            return new ResolvedSurfaceMaterial(
                surface,
                new ResolvedMaterial(
                    MaterialType.VertexColor,
                    TexturePayload: null,
                    TextureSourceKind.Bundled,
                    MaterialProjection.Uv,
                    Family: null,
                    TextureScale: null,
                    ReuseScope: MaterialReuseScope.PerObject),
                DefaultTerrainAlignedMaterialDepthOffset);
        }

        bool preferUvProjection = ShouldPreferUvProjection(
            cityObject.PackageName,
            surface,
            cityObjectOrigin,
            cityObjectCartesian);
        ResolvedMaterial resolvedMaterial = materialResolver.ResolveMaterial(new DefaultMaterialRequest(
            cityObject.PackageName,
            surface.TexturePayload,
            preferUvProjection,
            FamilyOverride: null,
            VariantSelectionKey: $"{cityObject.SlotKey}:{(preferUvProjection ? "uv" : "triplanar")}",
            BuildingAttributes: cityObject.BuildingAttributes,
            FloorsAboveGround: cityObject.FloorsAboveGround,
            MeasuredHeightMeters: cityObject.MeasuredHeightMeters,
            GeometryHeightMeters: cityObject.GeometryHeightMeters,
            FootprintAreaSquareMeters: cityObject.BuildingAttributes is null
                ? null
                : BuildingAttributeQueries.TryGetKnownPositiveMetric(cityObject.BuildingAttributes.BuildingFootprintArea),
            SurfaceRole: ToDefaultMaterialSurfaceRole(surface.Semantic)));
        MaterialDepthOffset? depthOffset = cityObject.TerrainAligned
            ? DefaultTerrainAlignedMaterialDepthOffset
            : null;
        return new ResolvedSurfaceMaterial(surface, resolvedMaterial, depthOffset);
    }

    private static ResolvedMaterial? TryCreateRoofTerrainTextureMaterial(
        string actualMeshCode,
        string packageName,
        ParsedSurface surface,
        double cityObjectMinAltitude,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (demTerrainTextureOverlay is null
            || TerrainOverlayMeshCodeResolver.ResolveMeshCode(actualMeshCode, demTerrainTextureOverlay) is null
            || surface.TexturePayload is not null
            || !IsBuildingPackage(packageName)
            || !IsRoofTerrainTextureSurface(surface, cityObjectMinAltitude, cityObjectOrigin, cityObjectCartesian))
        {
            return null;
        }

        return new ResolvedMaterial(
            MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind.Dataset,
            MaterialProjection.Uv,
            Family: null,
            TextureScale: null,
            ReuseScope: MaterialReuseScope.PerObject,
            TerrainOverlay: demTerrainTextureOverlay);
    }

    private static bool IsRoofTerrainTextureSurface(
        ParsedSurface surface,
        double cityObjectMinAltitude,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (surface.Semantic == ParsedSurfaceSemantic.Roof)
        {
            return true;
        }

        if (surface.Semantic is not (ParsedSurfaceSemantic.Unknown
            or ParsedSurfaceSemantic.Ground
            or ParsedSurfaceSemantic.OuterCeiling
            or ParsedSurfaceSemantic.OuterFloor))
        {
            return false;
        }

        Float3? normal = ComputeSurfaceNormal(surface, cityObjectOrigin, cityObjectCartesian);
        return normal is not null
            && Math.Abs(normal.Y) >= 0.98
            && IsAboveCityObjectBottomAltitude(surface, cityObjectMinAltitude);
    }

    private static bool IsAboveCityObjectBottomAltitude(
        ParsedSurface surface,
        double cityObjectMinAltitude)
    {
        double surfaceMinAltitude = surface.Vertices.Min(static vertex => vertex.Altitude);
        return surfaceMinAltitude > cityObjectMinAltitude + UnknownRoofBottomAltitudeToleranceMeters;
    }

    private static void TriangulateSurface(
        string packageName,
        ParsedSurface surface,
        ResolvedMaterial material,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        FacadeUvProjectionContext? facadeUvProjectionContext,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        DemUvProjection? demUvProjection,
        List<MeshVertex> vertices,
        List<int> indices)
    {
        List<(MeshVertex First, MeshVertex Second, MeshVertex Third, string SortKey)> triangles = [];
        bool useVertexColors = material.MaterialType == MaterialType.VertexColor;
        DemUvProjection? generatedDemUvProjection = material.TerrainOverlay is not null ? demUvProjection : null;
        bool useGeneratedDemUv = generatedDemUvProjection is not null;
        SurfaceUvProjection? generatedSurfaceUvProjection = !useGeneratedDemUv
            && surface.TexturePayload is null
            && material.Projection == MaterialProjection.Uv
                ? CreateGeneratedSurfaceUvProjection(
                    surface,
                    packageName,
                    cityObjectOrigin,
                    cityObjectCartesian,
                    facadeUvProjectionContext)
                : null;
        List<TessellatedRing> tessellatedRings = CreateSurfaceTessellatedRings(
            surface,
            cityObjectOrigin,
            cityObjectCartesian,
            globalOriginPoint,
            globalCartesian,
            generatedDemUvProjection,
            generatedSurfaceUvProjection,
            useVertexColors ? surface.BaseColor : null);
        if (tessellatedRings.Count == 0)
        {
            return;
        }

        Float3? expectedNormal = ComputePolygonNormal(tessellatedRings[0].Vertices.Select(static vertex => vertex.Position));
        if (expectedNormal is null)
        {
            return;
        }

        (Float3 planeOrigin, Float3 basisX, Float3 basisY) = CreateSurfacePlane(tessellatedRings[0].Vertices);
        Tess tessellator = new();

        foreach (TessellatedRing ring in tessellatedRings)
        {
            ContourVertex[] contour = ring.Vertices
                .Select(vertex => CreateContourVertex(vertex, planeOrigin, basisX, basisY))
                .ToArray();
            tessellator.AddContour(contour, ContourOrientation.Original);
        }

        tessellator.Tessellate(
            WindingRule.EvenOdd,
            ElementType.Polygons,
            polySize: 3,
            CombineTessVertexData);

        for (int triangleIndex = 0; triangleIndex < tessellator.ElementCount; triangleIndex++)
        {
            int elementBaseIndex = triangleIndex * 3;
            int element0 = tessellator.Elements[elementBaseIndex];
            int element1 = tessellator.Elements[elementBaseIndex + 1];
            int element2 = tessellator.Elements[elementBaseIndex + 2];
            if (element0 < 0 || element1 < 0 || element2 < 0)
            {
                continue;
            }

            TessVertexPayload vertex0 = GetTessVertexPayload(tessellator, element0);
            TessVertexPayload vertex1 = GetTessVertexPayload(tessellator, element1);
            TessVertexPayload vertex2 = GetTessVertexPayload(tessellator, element2);

            Float3 position0 = vertex0.Position;
            Float3 position1 = vertex1.Position;
            Float3 position2 = vertex2.Position;
            Float2 uv0 = vertex0.UV;
            Float2 uv1 = vertex1.UV;
            Float2 uv2 = vertex2.UV;
            ColorRgba? color0 = vertex0.Color;
            ColorRgba? color1 = vertex1.Color;
            ColorRgba? color2 = vertex2.Color;

            Float3? triangleNormal = ComputeNormal(position0, position1, position2);
            if (triangleNormal is null)
            {
                continue;
            }

            if (Dot(triangleNormal, expectedNormal) < 0.0)
            {
                (position1, position2) = (position2, position1);
                (uv1, uv2) = (uv2, uv1);
                (color1, color2) = (color2, color1);
                triangleNormal = ComputeNormal(position0, position1, position2);
                if (triangleNormal is null)
                {
                    continue;
                }
            }

            Float3? resoniteNormal = ComputeNormal(position0, position2, position1);
            if (resoniteNormal is null)
            {
                continue;
            }

            if (string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase)
                && resoniteNormal.Y < 0.0)
            {
                (position1, position2) = (position2, position1);
                (uv1, uv2) = (uv2, uv1);
                (color1, color2) = (color2, color1);
                resoniteNormal = ComputeNormal(position0, position2, position1);
                if (resoniteNormal is null)
                {
                    continue;
                }
            }

            (MeshVertex first, MeshVertex second, MeshVertex third, string sortKey) =
                CreateCanonicalSurfaceTriangle(
                    CreateMeshVertex(position0, resoniteNormal, uv0, color0),
                    CreateMeshVertex(position1, resoniteNormal, uv1, color1),
                    CreateMeshVertex(position2, resoniteNormal, uv2, color2));
            triangles.Add((first, second, third, sortKey));
        }

        triangles.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.SortKey, right.SortKey));
        foreach ((MeshVertex first, MeshVertex second, MeshVertex third, _) in triangles)
        {
            int baseIndex = vertices.Count;
            vertices.Add(first);
            vertices.Add(second);
            vertices.Add(third);
            indices.Add(baseIndex);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 1);
        }
    }

    private static (
        MeshVertex First,
        MeshVertex Second,
        MeshVertex Third,
        string SortKey) CreateCanonicalSurfaceTriangle(
        MeshVertex first,
        MeshVertex second,
        MeshVertex third)
    {
        (MeshVertex First, MeshVertex Second, MeshVertex Third) best = (first, second, third);
        string bestKey = CreateTriangleSortKey(first, second, third);

        string rotatedLeftKey = CreateTriangleSortKey(second, third, first);
        if (StringComparer.Ordinal.Compare(rotatedLeftKey, bestKey) < 0)
        {
            best = (second, third, first);
            bestKey = rotatedLeftKey;
        }

        string rotatedRightKey = CreateTriangleSortKey(third, first, second);
        if (StringComparer.Ordinal.Compare(rotatedRightKey, bestKey) < 0)
        {
            best = (third, first, second);
            bestKey = rotatedRightKey;
        }

        return (best.First, best.Second, best.Third, bestKey);
    }

    private static string CreateTriangleSortKey(
        MeshVertex first,
        MeshVertex second,
        MeshVertex third)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{CreateVertexSortKey(first)}|{CreateVertexSortKey(second)}|{CreateVertexSortKey(third)}");
    }

    private static string CreateVertexSortKey(MeshVertex vertex)
    {
        ColorRgba? color = vertex.Color;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{vertex.Position.X:R},{vertex.Position.Y:R},{vertex.Position.Z:R}|"
            + $"{vertex.Normal.X:R},{vertex.Normal.Y:R},{vertex.Normal.Z:R}|"
            + $"{vertex.UV0.X:R},{vertex.UV0.Y:R}|"
            + $"{color?.R ?? double.NaN:R},{color?.G ?? double.NaN:R},{color?.B ?? double.NaN:R},{color?.A ?? double.NaN:R}");
    }

    private static MeshVertex CreateMeshVertex(
        Float3 position,
        Float3 normal,
        Float2 uv,
        ColorRgba? color)
    {
        return new MeshVertex(
            ToContractFloat3(position),
            ToContractFloat3(normal),
            ToContractFloat2(uv),
            color is null ? null : ToContractColor(color));
    }

    private static List<TessellatedRing> CreateSurfaceTessellatedRings(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        DemUvProjection? generatedDemUvProjection,
        SurfaceUvProjection? generatedSurfaceUvProjection,
        ColorRgba? vertexColor)
    {
        List<TessellatedRing> rings =
        [
            CreateTessellatedRing(
                surface.ExteriorRing,
                cityObjectOrigin,
                cityObjectCartesian,
                globalOriginPoint,
                globalCartesian,
                generatedDemUvProjection,
                generatedSurfaceUvProjection,
                vertexColor),
        ];
        rings.AddRange(surface.InteriorRings.Select(ring => CreateTessellatedRing(
            ring,
            cityObjectOrigin,
            cityObjectCartesian,
            globalOriginPoint,
            globalCartesian,
            generatedDemUvProjection,
            generatedSurfaceUvProjection,
            vertexColor)));
        return rings.Where(static ring => ring.Vertices.Count >= 3).ToList();
    }

    private static TessellatedRing CreateTessellatedRing(
        ParsedRing ring,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        DemUvProjection? generatedDemUvProjection,
        SurfaceUvProjection? generatedSurfaceUvProjection,
        ColorRgba? vertexColor)
    {
        TessellatedVertex[] vertices = ring.Vertices
            .Select((point, index) => new TessellatedVertex(
                CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian),
                generatedDemUvProjection is not null
                    ? CreateGeneratedDemUv(point, generatedDemUvProjection.Value)
                    : ring.UVs is not null && index < ring.UVs.Count
                        ? ToInternalFloat2(ring.UVs[index])
                        : generatedSurfaceUvProjection is not null
                        ? CreateGeneratedSurfaceUv(point, cityObjectOrigin, cityObjectCartesian, generatedSurfaceUvProjection)
                        : new Float2(0.0, 0.0),
                vertexColor))
            .ToArray();
        return new TessellatedRing(ring.RingId, vertices);
    }

    private static Float2 CreateGeneratedDemUv(
        GeodeticPoint point,
        DemUvProjection demUvProjection)
    {
        double pointX = WebMercatorTileMath.LongitudeToNormalizedX(point.Longitude);
        double pointY = WebMercatorTileMath.LatitudeToNormalizedY(point.Latitude);
        double u = (pointX - demUvProjection.West) / demUvProjection.Width;
        double v = (demUvProjection.South - pointY) / demUvProjection.Height;

        return new Float2(u, v);
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

    private static SurfaceUvProjection? CreateGeneratedSurfaceUvProjection(
        ParsedSurface surface,
        string packageName,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        FacadeUvProjectionContext? facadeUvProjectionContext)
    {
        Float3[] positions = surface.ExteriorRing.Vertices
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        if (positions.Length < 3)
        {
            return null;
        }

        Float3? normal = ComputePolygonNormal(positions);
        if (normal is null)
        {
            return null;
        }

        SurfaceUvAxes? surfaceAxes = TryCreatePathAlignedSurfaceUvAxes(packageName, positions, normal)
            ?? TryCreateSurfaceUvAxes(normal);
        if (surfaceAxes is null)
        {
            return null;
        }

        double uvScale = PlateauPackageCatalog.IsBuildingPackage(packageName)
            ? 1.0 / Math.Max(facadeUvProjectionContext?.FloorHeightMeters ?? FacadeFloorMetrics.DefaultFloorUnitMeters, 1e-6)
            : 1.0;
        double vOffset = PlateauPackageCatalog.IsBuildingPackage(packageName)
            ? facadeUvProjectionContext is { } context
                ? -(context.MinimumY * uvScale)
                : -(positions.Min(static position => position.Y) * uvScale)
            : 0.0;
        return new SurfaceUvProjection(
            Scale(surfaceAxes.AxisU, uvScale),
            Scale(surfaceAxes.AxisV, uvScale),
            vOffset);
    }

    private static Float2 CreateGeneratedSurfaceUv(
        GeodeticPoint point,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        SurfaceUvProjection projection)
    {
        Float3 position = CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian);
        double u = Dot(position, projection.AxisU);
        double v = Dot(position, projection.AxisV) + projection.OffsetV;
        return new Float2(u, v);
    }

    private static Float3 Scale(Float3 value, double scalar)
    {
        return new Float3(
            value.X * scalar,
            value.Y * scalar,
            value.Z * scalar);
    }

    private static SurfaceUvAxes? TryCreateSurfaceUvAxes(Float3 normal)
    {
        Float3 verticalAxis = new(0.0, 1.0, 0.0);
        Float3 facadeAxisU = Cross(verticalAxis, normal);
        if (Magnitude(facadeAxisU) >= 1e-8)
        {
            return new SurfaceUvAxes(Normalize(facadeAxisU), verticalAxis);
        }

        Float3[] referenceAxes =
        [
            new Float3(1.0, 0.0, 0.0),
            new Float3(0.0, 0.0, 1.0),
            verticalAxis,
        ];

        foreach (Float3 referenceAxis in referenceAxes.OrderBy(axis => Math.Abs(Dot(normal, axis))))
        {
            Float3 axisU = Cross(referenceAxis, normal);
            if (Magnitude(axisU) < 1e-8)
            {
                continue;
            }

            axisU = Normalize(axisU);
            Float3 axisV = Cross(normal, axisU);
            if (Magnitude(axisV) < 1e-8)
            {
                continue;
            }

            return new SurfaceUvAxes(axisU, Normalize(axisV));
        }

        return null;
    }

    private static SurfaceUvAxes? TryCreatePathAlignedSurfaceUvAxes(
        string packageName,
        Float3[] positions,
        Float3 normal)
    {
        if (!PlateauPackageCatalog.IsPathLikePackage(packageName)
            || positions.Length < 2
            || Math.Abs(normal.Y) < 0.7)
        {
            return null;
        }

        Float3 axisU = Subtract(positions[1], positions[0]);
        double axisULength = 0.0;
        for (int index = 0; index < positions.Length; index++)
        {
            Float3 start = positions[index];
            Float3 end = positions[(index + 1) % positions.Length];
            Float3 edge = Subtract(end, start);
            Float3 planarEdge = Subtract(edge, Multiply(normal, Dot(edge, normal)));
            double edgeLength = Magnitude(planarEdge);
            if (edgeLength <= axisULength)
            {
                continue;
            }

            axisU = planarEdge;
            axisULength = edgeLength;
        }

        if (axisULength < 1e-8)
        {
            return null;
        }

        axisU = Normalize(axisU);
        Float3 axisV = Cross(normal, axisU);
        if (Magnitude(axisV) < 1e-8)
        {
            return null;
        }

        return new SurfaceUvAxes(axisU, Normalize(axisV));
    }

    private static bool IsBuildingPackage(string packageName)
    {
        return string.Equals(packageName, "bldg", StringComparison.Ordinal)
            || string.Equals(packageName, "ubld", StringComparison.Ordinal);
    }

    private static (Float3 Origin, Float3 BasisX, Float3 BasisY) CreateSurfacePlane(
        IReadOnlyList<TessellatedVertex> vertices)
    {
        Float3 origin = vertices[0].Position;
        Float3? normal = ComputePolygonNormal(vertices.Select(static vertex => vertex.Position))
            ?? throw new PlateauImportValidationException(["Failed to resolve a polygon plane for tessellation."]);

        Float3? basisX = null;
        foreach (TessellatedVertex vertex in vertices.Skip(1))
        {
            Float3 candidate = Subtract(vertex.Position, origin);
            if (Magnitude(candidate) >= 1e-8)
            {
                basisX = Normalize(candidate);
                break;
            }
        }

        if (basisX is null)
        {
            throw new PlateauImportValidationException(["Failed to resolve a polygon basis for tessellation."]);
        }

        Float3 basisY = Normalize(Cross(normal, basisX));
        return (origin, basisX, basisY);
    }

    private static ContourVertex CreateContourVertex(
        TessellatedVertex vertex,
        Float3 planeOrigin,
        Float3 basisX,
        Float3 basisY)
    {
        Float3 delta = Subtract(vertex.Position, planeOrigin);
        double projectedX = Dot(delta, basisX);
        double projectedY = Dot(delta, basisY);

        return new ContourVertex
        {
            Position = new Vec3((float)projectedX, (float)projectedY, 0.0f),
            Data = new TessVertexPayload(vertex.Position, vertex.UV, vertex.Color),
        };
    }

    private static object CombineTessVertexData(Vec3 position, object[] data, float[] weights)
    {
        double x = 0.0;
        double y = 0.0;
        double z = 0.0;
        double u = 0.0;
        double v = 0.0;
        double r = 0.0;
        double g = 0.0;
        double b = 0.0;
        double a = 0.0;
        bool hasColor = false;

        for (int index = 0; index < data.Length; index++)
        {
            if (data[index] is not TessVertexPayload vertexData)
            {
                continue;
            }

            double weight = weights[index];
            x += vertexData.Position.X * weight;
            y += vertexData.Position.Y * weight;
            z += vertexData.Position.Z * weight;
            u += vertexData.UV.X * weight;
            v += vertexData.UV.Y * weight;
            if (vertexData.Color is not null)
            {
                hasColor = true;
                r += vertexData.Color.R * weight;
                g += vertexData.Color.G * weight;
                b += vertexData.Color.B * weight;
                a += vertexData.Color.A * weight;
            }
        }

        return new TessVertexPayload(
            new Float3(x, y, z),
            new Float2(u, v),
            hasColor ? new ColorRgba(r, g, b, a) : null);
    }

    private static TessVertexPayload GetTessVertexPayload(Tess tessellator, int elementIndex)
    {
        return tessellator.Vertices[elementIndex].Data as TessVertexPayload
            ?? throw new PlateauImportValidationException(["Polygon tessellation produced a vertex without payload data."]);
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

    private static Float3? ComputeNormal(
        Float3 position0,
        Float3 position1,
        Float3 position2)
    {
        double ax = position1.X - position0.X;
        double ay = position1.Y - position0.Y;
        double az = position1.Z - position0.Z;
        double bx = position2.X - position0.X;
        double by = position2.Y - position0.Y;
        double bz = position2.Z - position0.Z;

        double crossX = ay * bz - az * by;
        double crossY = az * bx - ax * bz;
        double crossZ = ax * by - ay * bx;
        double magnitude = Math.Sqrt((crossX * crossX) + (crossY * crossY) + (crossZ * crossZ));

        if (magnitude < 1e-8)
        {
            return null;
        }

        return new Float3(crossX / magnitude, crossY / magnitude, crossZ / magnitude);
    }

    private static Float3 Subtract(Float3 left, Float3 right)
    {
        return new Float3(
            left.X - right.X,
            left.Y - right.Y,
            left.Z - right.Z);
    }

    private static Float3 Add(Float3 left, Float3 right)
    {
        return new Float3(
            left.X + right.X,
            left.Y + right.Y,
            left.Z + right.Z);
    }

    private static Float3 Multiply(Float3 vector, double scalar)
    {
        return new Float3(
            vector.X * scalar,
            vector.Y * scalar,
            vector.Z * scalar);
    }

    private static Float3 Cross(Float3 left, Float3 right)
    {
        return new Float3(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));
    }

    private static double Dot(Float3 left, Float3 right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }

    private static double Magnitude(Float3 vector)
    {
        return Math.Sqrt(Dot(vector, vector));
    }

    private static double Distance(Float3 left, Float3 right)
    {
        return Math.Sqrt(DistanceSquared(left, right));
    }

    private static double DistanceSquared(Float3 left, Float3 right)
    {
        double deltaX = left.X - right.X;
        double deltaY = left.Y - right.Y;
        double deltaZ = left.Z - right.Z;
        return (deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ);
    }

    private static Float3 Normalize(Float3 vector)
    {
        double magnitude = Magnitude(vector);
        if (magnitude < 1e-8)
        {
            throw new PlateauImportValidationException(["Attempted to normalize a zero-length polygon vector."]);
        }

        return new Float3(
            vector.X / magnitude,
            vector.Y / magnitude,
            vector.Z / magnitude);
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

    private static bool ShouldPreferUvProjection(
        string packageName,
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (surface.TexturePayload is not null)
        {
            return true;
        }

        if (string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsBuildingPackage(packageName))
        {
            return PlateauPackageCatalog.IsPathLikePackage(packageName)
                && IsNearHorizontalSurface(surface, cityObjectOrigin, cityObjectCartesian);
        }

        if (surface.Semantic is ParsedSurfaceSemantic.Wall)
        {
            return true;
        }

        if (surface.Semantic is ParsedSurfaceSemantic.Roof
            or ParsedSurfaceSemantic.Ground
            or ParsedSurfaceSemantic.OuterCeiling
            or ParsedSurfaceSemantic.OuterFloor)
        {
            return false;
        }

        return IsFacadeSurface(surface, cityObjectOrigin, cityObjectCartesian);
    }

    private static DefaultMaterialSurfaceRole ToDefaultMaterialSurfaceRole(ParsedSurfaceSemantic semantic)
    {
        return semantic switch
        {
            ParsedSurfaceSemantic.Wall => DefaultMaterialSurfaceRole.Wall,
            ParsedSurfaceSemantic.Roof => DefaultMaterialSurfaceRole.Roof,
            ParsedSurfaceSemantic.Ground => DefaultMaterialSurfaceRole.Ground,
            ParsedSurfaceSemantic.Closure => DefaultMaterialSurfaceRole.Closure,
            ParsedSurfaceSemantic.OuterCeiling => DefaultMaterialSurfaceRole.OuterCeiling,
            ParsedSurfaceSemantic.OuterFloor => DefaultMaterialSurfaceRole.OuterFloor,
            _ => DefaultMaterialSurfaceRole.Unknown,
        };
    }

    private static bool IsFacadeSurface(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        Float3[] positions = surface.Vertices
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        Float3? normal = ComputePolygonNormal(positions);
        if (normal is null)
        {
            return false;
        }

        return Math.Abs(normal.Y) < 0.45;
    }

    private static bool IsNearHorizontalSurface(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        Float3[] positions = surface.Vertices
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        Float3? normal = ComputePolygonNormal(positions);
        return normal is not null && Math.Abs(normal.Y) >= 0.98;
    }

    private static bool ShouldCullSurfaceBeforeProjection(
        string packageName,
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (!IsBuildingPackage(packageName))
        {
            return false;
        }

        return IsDownwardNearHorizontalSurface(surface, cityObjectOrigin, cityObjectCartesian);
    }

    private static HashSet<string> GetCulledSurfaceIdsBeforeProjection(
        string packageName,
        IEnumerable<ParsedSurface> surfaces,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (!IsBuildingPackage(packageName))
        {
            return [];
        }

        SurfaceProjectionInfo[] candidates = surfaces
            .Select(surface => CreateSurfaceProjectionInfo(surface, cityObjectOrigin, cityObjectCartesian))
            .Where(static info => info.MinimumY.HasValue && info.MaximumY.HasValue)
            .ToArray();

        if (candidates.Length == 0)
        {
            return [];
        }

        double objectMinimumY = candidates.Min(static info => info.MinimumY!.Value);
        double objectMaximumY = candidates.Max(static info => info.MaximumY!.Value);

        return candidates
            .Where(static info => IsBottomBandCullCandidate(info))
            .Where(info => info.MaximumY!.Value <= objectMinimumY + BuildingBottomCullBandMeters)
            .Where(info => objectMaximumY > info.MaximumY!.Value + BuildingBottomCullBandMeters)
            .Select(static info => info.Surface.PolygonId)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsBottomBandCullCandidate(SurfaceProjectionInfo info)
    {
        return info.IsNearHorizontal;
    }

    private static bool IsDownwardNearHorizontalSurface(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (ComputeSurfaceNormal(surface, cityObjectOrigin, cityObjectCartesian) is not Float3 normal)
        {
            return false;
        }

        return Math.Abs(normal.Y) >= 0.98
            && normal.Y <= -0.98;
    }

    private static Float3? ComputeSurfaceNormal(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        Float3[] positions = surface.Vertices
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        return ComputePolygonNormal(positions);
    }

    private static SurfaceProjectionInfo CreateSurfaceProjectionInfo(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        Float3[] positions = surface.Vertices
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        if (positions.Length == 0)
        {
            return new SurfaceProjectionInfo(surface, null, null, false, false);
        }

        Float3? normal = ComputePolygonNormal(positions);
        bool isNearHorizontal = normal is not null && Math.Abs(normal.Y) >= 0.98;
        bool isDownwardNearHorizontal = isNearHorizontal && normal is not null && normal.Y <= -0.98;

        return new SurfaceProjectionInfo(
            surface,
            positions.Min(static position => position.Y),
            positions.Max(static position => position.Y),
            isNearHorizontal,
            isDownwardNearHorizontal);
    }

    private static FacadeUvProjectionContext? TryCreateFacadeUvProjectionContext(
        string packageName,
        IEnumerable<ParsedSurface> surfaces,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (!IsBuildingPackage(packageName))
        {
            return null;
        }

        SurfaceProjectionInfo[] surfaceInfos = surfaces
            .Select(surface => CreateSurfaceProjectionInfo(surface, cityObjectOrigin, cityObjectCartesian))
            .Where(static info => info.MinimumY.HasValue && info.MaximumY.HasValue)
            .ToArray();
        if (surfaceInfos.Length == 0)
        {
            return null;
        }

        SurfaceProjectionInfo[] contextSurfaceInfos = surfaceInfos
            .Where(static info => !IsGeneratedLod1RoofSurface(info.Surface))
            .ToArray();
        if (contextSurfaceInfos.Length == 0)
        {
            contextSurfaceInfos = surfaceInfos;
        }

        (double minimumY, double maximumY) = ResolveFacadeUvVerticalRange(contextSurfaceInfos, surfaceInfos);
        double geometryHeightMeters = Math.Max(maximumY - minimumY, 0.0);
        int floorCount = Math.Max(
            1,
            (int)Math.Ceiling(Math.Max(geometryHeightMeters, FacadeFloorMetrics.DefaultFloorUnitMeters) / FacadeFloorMetrics.DefaultFloorUnitMeters));
        double floorHeightMeters = Math.Max(
            geometryHeightMeters / floorCount,
            1e-6);

        return new FacadeUvProjectionContext(
            minimumY,
            maximumY,
            floorHeightMeters,
            floorCount);
    }

    private static (double MinimumY, double MaximumY) ResolveFacadeUvVerticalRange(
        IReadOnlyList<SurfaceProjectionInfo> contextSurfaceInfos,
        IReadOnlyList<SurfaceProjectionInfo> allSurfaceInfos)
    {
        double minimumY = contextSurfaceInfos.Min(static info => info.MinimumY!.Value);
        double maximumY = contextSurfaceInfos.Max(static info => info.MaximumY!.Value);
        if (maximumY - minimumY > 1e-6 || contextSurfaceInfos.Count == allSurfaceInfos.Count)
        {
            return (minimumY, maximumY);
        }

        double fallbackMinimumY = allSurfaceInfos.Min(static info => info.MinimumY!.Value);
        double fallbackMaximumY = allSurfaceInfos.Max(static info => info.MaximumY!.Value);
        return fallbackMaximumY - fallbackMinimumY > maximumY - minimumY
            ? (fallbackMinimumY, fallbackMaximumY)
            : (minimumY, maximumY);
    }

    private static bool IsGeneratedLod1RoofSurface(ParsedSurface surface)
    {
        return surface.PolygonId.Contains("_generated_shed-", StringComparison.Ordinal)
            || surface.PolygonId.Contains("_generated_gable-", StringComparison.Ordinal)
            || surface.PolygonId.Contains("_generated_hip-", StringComparison.Ordinal);
    }

    private readonly record struct SurfaceProjectionInfo(
        ParsedSurface Surface,
        double? MinimumY,
        double? MaximumY,
        bool IsNearHorizontal,
        bool IsDownwardNearHorizontal);

    private static bool IsGeneratedRoadMarkingSurface(ParsedSurface surface)
    {
        return surface.PolygonId.Contains("_generated_marking", StringComparison.Ordinal);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string? GetAttribute(XElement element, XName attributeName)
    {
        return element.Attribute(attributeName)?.Value;
    }

    private static bool AreSamePoint(GeodeticPoint left, GeodeticPoint right)
    {
        return Math.Abs(left.Latitude - right.Latitude) < 1e-8
            && Math.Abs(left.Longitude - right.Longitude) < 1e-8
            && Math.Abs(left.Altitude - right.Altitude) < 1e-8;
    }

    private static bool HasExplicitMaterialColor(ColorRgba color)
    {
        return Math.Abs(color.R - DefaultMaterialColor.R) >= 1e-8
            || Math.Abs(color.G - DefaultMaterialColor.G) >= 1e-8
            || Math.Abs(color.B - DefaultMaterialColor.B) >= 1e-8
            || Math.Abs(color.A - DefaultMaterialColor.A) >= 1e-8;
    }

    private static string SanitizeIdentifier(string value)
    {
        return string.Concat(
            value.Select(character => char.IsLetterOrDigit(character) ? character : '_'));
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
        ArgumentNullException.ThrowIfNull(sourceFile);
        ArgumentNullException.ThrowIfNull(referenceSystem);
        ArgumentNullException.ThrowIfNull(globalOriginPoint);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(materialResolver);

        CoordinateReferenceSystem projectionReferenceSystem = referenceSystem.ToProjectionModel();
        ValidateCompatibleReferenceSystem(
            projectionReferenceSystem,
            sourceFile.CityObjects.FirstOrDefault()?.ReferenceSystem.ToProjectionModel() ?? projectionReferenceSystem);

        global::PlateauResoniteLink.Application.Importing.ParsedCityObject[] projectedInputCityObjects =
            global::PlateauResoniteLink.Application.Importing.DemCityObjectAggregation.AggregateBySourceFileAndThirdMesh(
                sourceFile.SourceFile,
                sourceFile.CityObjects);

        foreach (global::PlateauResoniteLink.Application.Importing.ParsedCityObject parsedCityObject in projectedInputCityObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (predicate is not null && !predicate(parsedCityObject))
            {
                continue;
            }

            foreach (ImportedCityObject cityObject in ProjectParsedCityObject(
                         parsedCityObject,
                         globalOriginPoint,
                         globalCartesian,
                         demTerrainTextureOverlays,
                         requestedMeshCodeBounds,
                         terrainHeightSampler: null,
                         request,
                         materialResolver,
                         progressReporter,
                         cancellationToken))
            {
                yield return cityObject;
            }
        }
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
        ArgumentNullException.ThrowIfNull(parsedCityObject);
        ArgumentNullException.ThrowIfNull(globalOriginPoint);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(materialResolver);

        double? geometryHeightMeters = ResolveParsedGeometryHeightMeters(parsedCityObject.Surfaces);
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject terrainAlignedParsedCityObject =
            ApplyGeneratedLod1Roof(ConformCityObjectToTerrain(parsedCityObject, terrainHeightSampler)) with
            {
                GeometryHeightMeters = geometryHeightMeters,
            };
        List<ImportedCityObject> projectedCityObjects = [];
        List<ImportedCityObject> generatedRoadMarkings = [];

        foreach ((global::PlateauResoniteLink.Application.Importing.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) splitCityObject
                 in TerrainOverlayProjectionSplitPolicy.SplitParsedCityObject(
                     terrainAlignedParsedCityObject,
                     demTerrainTextureOverlays,
                     requestedMeshCodeBounds,
                     progressReporter,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TerrainOverlayProjectionSplitPolicy.ShouldProjectSplit(
                    splitCityObject.CityObject.ActualMeshCode,
                    request.MeshCode,
                    requestedMeshCodeBounds,
                    splitCityObject.Overlay))
            {
                throw CreateTerrainOverlayMeshCodeMismatchException(
                    "project",
                    splitCityObject.CityObject.ActualMeshCode,
                    request.MeshCode,
                    requestedMeshCodeBounds,
                    splitCityObject.Overlay);
            }

            ImportedCityObject cityObject = ProjectTerrainMeshModeCityObject(
                splitCityObject.CityObject,
                globalOriginPoint,
                globalCartesian,
                splitCityObject.Overlay,
                request,
                requestedMeshCodeBounds,
                materialResolver,
                progressReporter,
                cancellationToken);

            if (HasRenderableGeometry(cityObject))
            {
                projectedCityObjects.Add(cityObject);
            }

            global::PlateauResoniteLink.Application.Importing.GeodeticPoint markingOrigin = ResolveParsedCityObjectOrigin(splitCityObject.CityObject);
            LocalCartesian? markingCartesian = splitCityObject.CityObject.ReferenceSystem.IsGeographic
                ? new LocalCartesian(
                    markingOrigin.Latitude,
                    markingOrigin.Longitude,
                    markingOrigin.Altitude,
                    splitCityObject.CityObject.ReferenceSystem.Geocentric)
                : null;
            global::PlateauResoniteLink.Application.Importing.ParsedCityObject? roadMarkingCityObject = GeneratedRoadMarkingCityObjectFactory.Create(
                splitCityObject.CityObject,
                markingOrigin,
                markingCartesian);
            if (roadMarkingCityObject is null)
            {
                continue;
            }

            ImportedCityObject markingObject = ProjectCityObject(
                roadMarkingCityObject,
                globalOriginPoint,
                globalCartesian,
                splitCityObject.Overlay,
                materialResolver) with
            {
                CollisionEnabled = false,
            };
            if (HasRenderableGeometry(markingObject))
            {
                generatedRoadMarkings.Add(markingObject);
            }
        }

        ImportedCityObject[] alignedCityObjects =
            request.TerrainMeshMode is TerrainMeshMode.Grid or TerrainMeshMode.Dynamic
            && string.Equals(terrainAlignedParsedCityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
                ? AlignAdjacentDemTerrainGridChunkBoundaries(projectedCityObjects)
                : [.. projectedCityObjects];

        foreach (ImportedCityObject cityObject in alignedCityObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return cityObject;
        }

        foreach (ImportedCityObject markingObject in generatedRoadMarkings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return markingObject;
        }
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
        ArgumentNullException.ThrowIfNull(parsedCityObject);
        ArgumentNullException.ThrowIfNull(globalOriginPoint);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(materialResolver);

        double? geometryHeightMeters = ResolveParsedGeometryHeightMeters(parsedCityObject.Surfaces);
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject terrainAlignedParsedCityObject =
            ApplyGeneratedLod1Roof(ConformCityObjectToTerrain(parsedCityObject, terrainHeightSampler)) with
            {
                GeometryHeightMeters = geometryHeightMeters,
            };

        foreach ((global::PlateauResoniteLink.Application.Importing.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) splitCityObject
                 in TerrainOverlayProjectionSplitPolicy.SplitParsedCityObject(
                     terrainAlignedParsedCityObject,
                     demTerrainTextureOverlays,
                     requestedMeshCodeBounds))
        {
            if (!TerrainOverlayProjectionSplitPolicy.ShouldProjectSplit(
                    splitCityObject.CityObject.ActualMeshCode,
                    request.MeshCode,
                    requestedMeshCodeBounds ?? [],
                    splitCityObject.Overlay))
            {
                throw CreateTerrainOverlayMeshCodeMismatchException(
                    "common-material-enumeration",
                    splitCityObject.CityObject.ActualMeshCode,
                    request.MeshCode,
                    requestedMeshCodeBounds,
                    splitCityObject.Overlay);
            }

            global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin = ResolveParsedCityObjectOrigin(splitCityObject.CityObject);
            LocalCartesian? cityObjectCartesian = splitCityObject.CityObject.ReferenceSystem.IsGeographic
                ? new LocalCartesian(
                    cityObjectOrigin.Latitude,
                    cityObjectOrigin.Longitude,
                    cityObjectOrigin.Altitude,
                    splitCityObject.CityObject.ReferenceSystem.Geocentric)
                : null;

            foreach (MaterialBinding material in request.TerrainMeshMode is TerrainMeshMode.Grid or TerrainMeshMode.Dynamic
                         && string.Equals(splitCityObject.CityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
                            ? CityGmlSurfaceMaterialResolver.CreateDemTerrainGridMaterials(
                                splitCityObject.CityObject,
                                cityObjectOrigin,
                                cityObjectCartesian,
                                splitCityObject.Overlay,
                                request.MeshCode,
                                requestedMeshCodeBounds,
                                materialResolver)
                            : CityGmlSurfaceMaterialResolver.CreateSharedCommonMaterialBindings(
                                splitCityObject.CityObject,
                                cityObjectOrigin,
                                cityObjectCartesian,
                                splitCityObject.Overlay,
                                materialResolver))
            {
                yield return material;
            }
        }
    }

    private static ImportedCityObject ProjectTerrainMeshModeCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        PlateauImportRequest request,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        IDefaultMaterialResolver materialResolver,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        bool isDem = string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase);
        if (!isDem || request.TerrainMeshMode == TerrainMeshMode.Static)
        {
            return ProjectCityObject(cityObject, globalOriginPoint, globalCartesian, demTerrainTextureOverlay, materialResolver);
        }

        bool hasGrid = TryProjectDemTerrainGridCityObject(
            cityObject,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlay,
            request,
            requestedMeshCodeBounds,
            materialResolver,
            progressReporter,
            cancellationToken,
            out ImportedCityObject? heightMapCityObject);
        if (!hasGrid)
        {
            return request.TerrainMeshMode == TerrainMeshMode.Dynamic
                ? CreateNonRenderableCityObject(cityObject)
                : ProjectCityObject(cityObject, globalOriginPoint, globalCartesian, demTerrainTextureOverlay, materialResolver);
        }

        if (request.TerrainMeshMode == TerrainMeshMode.Grid)
        {
            return heightMapCityObject!;
        }

        ImportedCityObject staticCityObject = ProjectCityObject(
            cityObject,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlay,
            materialResolver);
        TriangleMeshGeometry staticMesh = AssertTriangleMeshGeometry(staticCityObject);
        TriangleMeshGeometry rebasedStaticMesh = RebaseTriangleMeshToTransform(
            staticMesh,
            staticCityObject.Transform,
            heightMapCityObject!.Transform);
        return heightMapCityObject with
        {
            Geometry = new DynamicTerrainGeometry(rebasedStaticMesh, AssertTerrainGridGeometry(heightMapCityObject)),
            Materials = staticCityObject.Materials,
        };
    }

    private static ImportedCityObject CreateNonRenderableCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject)
    {
        return new ImportedCityObject(
            cityObject.SlotKey,
            cityObject.DisplayName,
            cityObject.PackageName,
            cityObject.ActualMeshCode,
            cityObject.LodLevel,
            new Transform3D(new Float3(0.0, 0.0, 0.0)),
            new TriangleMeshGeometry(new ImportedMesh([], [])),
            [],
            SourceFileRelativePath: cityObject.SourceFileRelativePath);
    }

    private static global::PlateauResoniteLink.Application.Importing.ParsedCityObject ConformCityObjectToTerrain(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject parsedCityObject,
        ProjectionTerrainHeightSampler? terrainHeightSampler)
    {
        if (terrainHeightSampler is null
            || !ShouldTerrainAlignCityObject(parsedCityObject))
        {
            return parsedCityObject;
        }

        global::PlateauResoniteLink.Application.Importing.ParsedCityObject subdividedCityObject =
            SubdivideTerrainAlignedCityObject(parsedCityObject);
        bool terrainAligned = false;
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin = ResolveParsedCityObjectOrigin(subdividedCityObject);
        LocalCartesian? cityObjectCartesian = subdividedCityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                subdividedCityObject.ReferenceSystem.Geocentric)
            : null;
        global::PlateauResoniteLink.Application.Importing.ParsedSurface[] conformedSurfaces =
            PlateauPackageCatalog.IsRoadPackage(subdividedCityObject.PackageName)
                ? ConformRoadSurfacesToTerrainWithFallback(
                        subdividedCityObject.Surfaces.Select(global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.ToProjectionModel).ToArray(),
                        terrainHeightSampler,
                        ref terrainAligned)
                    .Select(global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.FromProjectionModel)
                    .ToArray()
                : ConformSurfacesToTerrain(
                        subdividedCityObject.PackageName,
                        subdividedCityObject.Surfaces.Select(global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.ToProjectionModel).ToArray(),
                        terrainHeightSampler,
                        cityObjectOrigin.ToProjectionModel(),
                        cityObjectCartesian,
                        ref terrainAligned)
                    .Select(global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.FromProjectionModel)
                    .ToArray();

        return terrainAligned
            ? subdividedCityObject with
            {
                Surfaces = conformedSurfaces,
                TerrainAligned = true,
            }
            : subdividedCityObject;
    }

    private static global::PlateauResoniteLink.Application.Importing.ParsedCityObject SubdivideTerrainAlignedCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject)
    {
        if (!ShouldSubdivideTerrainAlignedCityObject(cityObject))
        {
            return cityObject;
        }

        List<global::PlateauResoniteLink.Application.Importing.ParsedSurface> subdividedSurfaces = [];
        foreach (global::PlateauResoniteLink.Application.Importing.ParsedSurface surface in cityObject.Surfaces)
        {
            subdividedSurfaces.AddRange(SubdivideTransportationSurfaceForTerrainAlignment(surface, cityObject));
        }

        return subdividedSurfaces.Count == cityObject.Surfaces.Length
            ? cityObject
            : cityObject with { Surfaces = subdividedSurfaces.ToArray() };
    }

    private static bool ShouldSubdivideTerrainAlignedCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject)
    {
        return ShouldSubdivideTerrainAlignedCityObject(cityObject.PackageName, cityObject.LodLevel);
    }

    private static bool ShouldTerrainAlignCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject)
    {
        return ShouldTerrainAlignCityObject(cityObject.PackageName, cityObject.LodLevel);
    }

    private static bool ShouldSubdivideTerrainAlignedCityObject(string packageName, int? lodLevel)
    {
        return PlateauPackageCatalog.IsRoadPackage(packageName)
            && (!lodLevel.HasValue || lodLevel.Value < 3);
    }

    private static bool ShouldTerrainAlignCityObject(string packageName, int? lodLevel)
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

    private static MaterialBinding[] CreateCommonMaterialBindings(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        HashSet<string> culledSurfaceIds = GetCulledSurfaceIdsBeforeProjection(
            cityObject.PackageName,
            cityObject.Surfaces,
            cityObjectOrigin,
            cityObjectCartesian);
        double cityObjectMinAltitude = ResolveProjectionMinimumAltitude(cityObject.Surfaces);
        List<ResolvedSurfaceMaterial> resolvedSurfaces =
        [
            .. cityObject.Surfaces
                .Where(surface => !culledSurfaceIds.Contains(surface.PolygonId))
                .Select(surface => ResolveSurfaceMaterial(
                    cityObject,
                    cityObjectOrigin,
                    cityObjectCartesian,
                    surface,
                    cityObjectMinAltitude,
                    demTerrainTextureOverlay,
                    materialResolver)),
        ];

        return resolvedSurfaces
            .GroupBy(
                resolvedSurface => MaterialGroupingPolicy.CreateKey(
                    cityObject.ActualMeshCode,
                    resolvedSurface.Material,
                    resolvedSurface.DepthOffset,
                    resolvedSurface.Material.TextureScale,
                    resolvedSurface.Surface.BaseColor,
                    resolvedSurface.Material.TextureOffset))
            .OrderBy(static group => group.Min(static surface => ParsedSurfaceStableSortKey.Create(surface.Surface)), StringComparer.Ordinal)
            .Select((group, materialIndex) =>
            {
                ResolvedSurfaceMaterial representativeSurface = group.First();
                return CreateMaterialBinding(
                    cityObject.ActualMeshCode,
                    representativeSurface,
                    materialIndex);
            })
            .Where(static material => material.ReuseScope == MaterialReuseScope.Shared)
            .ToArray();
    }

    private static ImportedCityObject[] AlignAdjacentDemTerrainGridChunkBoundaries(
        IReadOnlyList<ImportedCityObject> cityObjects)
    {
        TerrainGridChunkAlignmentState?[] states = cityObjects
            .Select(static cityObject => TerrainGridChunkAlignmentState.TryCreate(cityObject))
            .ToArray();
        if (states.Any(static state => state is null))
        {
            return cityObjects.ToArray();
        }

        TerrainGridChunkAlignmentState[] chunkStates = states
            .Select(static state => state!)
            .ToArray();
        const double seaLevelWorldHeightTolerance = 1e-6;
        Dictionary<DemBoundarySampleKey, List<BoundaryHeightSampleReference>> sampleReferencesByKey = [];
        foreach (TerrainGridChunkAlignmentState state in chunkStates)
        {
            foreach (BoundaryHeightSampleReference sampleReference in EnumerateBoundaryHeightSampleReferences(state))
            {
                if (!sampleReferencesByKey.TryGetValue(sampleReference.Key, out List<BoundaryHeightSampleReference>? references))
                {
                    references = [];
                    sampleReferencesByKey.Add(sampleReference.Key, references);
                }

                references.Add(sampleReference);
            }
        }

        bool foundSharedBoundary = false;
        foreach (List<BoundaryHeightSampleReference> references in sampleReferencesByKey.Values)
        {
            if (references.Count < 2
                || references.Select(static reference => reference.State.CityObject).Distinct().Count() < 2)
            {
                continue;
            }

            foundSharedBoundary = true;
            double worldHeightSum = 0.0;
            int sampleCount = 0;
            double nonSeaLevelWorldHeightSum = 0.0;
            int nonSeaLevelSampleCount = 0;
            foreach (BoundaryHeightSampleReference reference in references)
            {
                double worldHeight = reference.State.BaseHeight + reference.State.HeightSamples[reference.SampleIndex];
                bool isSeaLevelFallbackCandidate = Math.Abs(worldHeight) <= seaLevelWorldHeightTolerance;
                worldHeightSum += worldHeight;
                sampleCount++;
                if (!isSeaLevelFallbackCandidate)
                {
                    nonSeaLevelWorldHeightSum += worldHeight;
                    nonSeaLevelSampleCount++;
                }
            }

            double alignedWorldHeight = nonSeaLevelSampleCount > 0
                ? nonSeaLevelWorldHeightSum / nonSeaLevelSampleCount
                : worldHeightSum / sampleCount;
            foreach (BoundaryHeightSampleReference reference in references)
            {
                reference.State.HeightSamples[reference.SampleIndex] = alignedWorldHeight - reference.State.BaseHeight;
            }
        }

        if (!foundSharedBoundary)
        {
            return cityObjects.ToArray();
        }

        return chunkStates
            .Select(static state => state.ToCityObject())
            .ToArray();
    }

    private static IEnumerable<BoundaryHeightSampleReference> EnumerateBoundaryHeightSampleReferences(
        TerrainGridChunkAlignmentState state)
    {
        int width = state.Geometry.Width;
        int height = state.Geometry.Height;
        if (width < 2 || height < 2)
        {
            yield break;
        }

        for (int row = 0; row < height; row++)
        {
            yield return CreateBoundaryHeightSampleReference(state, row, 0);
            yield return CreateBoundaryHeightSampleReference(state, row, width - 1);
        }

        for (int column = 1; column < width - 1; column++)
        {
            yield return CreateBoundaryHeightSampleReference(state, 0, column);
            yield return CreateBoundaryHeightSampleReference(state, height - 1, column);
        }
    }

    private static BoundaryHeightSampleReference CreateBoundaryHeightSampleReference(
        TerrainGridChunkAlignmentState state,
        int row,
        int column)
    {
        double u = state.Geometry.Width == 1 ? 0.0 : (double)column / (state.Geometry.Width - 1);
        double v = state.Geometry.Height == 1 ? 0.0 : (double)row / (state.Geometry.Height - 1);
        double x = (state.CityObject.Transform.Position.X - (state.Geometry.Size.X / 2.0)) + (state.Geometry.Size.X * u);
        double z = (state.CityObject.Transform.Position.Z - (state.Geometry.Size.Y / 2.0)) + (state.Geometry.Size.Y * v);
        int sampleIndex = (row * state.Geometry.Width) + column;
        return new BoundaryHeightSampleReference(
            state,
            sampleIndex,
            new DemBoundarySampleKey(
                QuantizeBoundaryCoordinate(x),
                QuantizeBoundaryCoordinate(z)));
    }

    private static long QuantizeBoundaryCoordinate(double coordinate)
    {
        const double boundaryTolerance = 1e-3;
        return (long)Math.Round(coordinate / boundaryTolerance, MidpointRounding.AwayFromZero);
    }

    private sealed class TerrainGridChunkAlignmentState
    {
        public TerrainGridChunkAlignmentState(
            ImportedCityObject cityObject,
            TerrainGridGeometry geometry,
            double[] heightSamples)
        {
            CityObject = cityObject;
            Geometry = geometry;
            HeightSamples = heightSamples;
            BaseHeight = cityObject.Transform.Position.Y - geometry.MaxHeight;
        }

        public ImportedCityObject CityObject { get; }

        public TerrainGridGeometry Geometry { get; }

        public double[] HeightSamples { get; }

        public double BaseHeight { get; }

        public static TerrainGridChunkAlignmentState? TryCreate(ImportedCityObject cityObject)
        {
            TerrainGridGeometry? geometry = cityObject.Geometry switch
            {
                TerrainGridGeometry terrainGrid => terrainGrid,
                DynamicTerrainGeometry dynamicTerrain => dynamicTerrain.GridMesh,
                _ => null,
            };
            return geometry is not null
                ? new TerrainGridChunkAlignmentState(cityObject, geometry, geometry.HeightSamples.ToArray())
                : null;
        }

        public ImportedCityObject ToCityObject()
        {
            double minHeight = HeightSamples.Min();
            double maxHeight = HeightSamples.Max();
            Transform3D alignedTransform = CityObject.Transform with
            {
                Position = CityObject.Transform.Position with
                {
                    Y = BaseHeight + maxHeight,
                },
            };

            return CityObject with
            {
                Transform = alignedTransform,
                Geometry = CityObject.Geometry switch
                {
                    DynamicTerrainGeometry dynamicTerrain => dynamicTerrain with
                    {
                        StaticMesh = RebaseTriangleMeshToTransform(
                            dynamicTerrain.StaticMesh,
                            CityObject.Transform,
                            alignedTransform),
                        GridMesh = dynamicTerrain.GridMesh with
                        {
                            MinHeight = minHeight,
                            MaxHeight = maxHeight,
                            HeightSamples = HeightSamples,
                        },
                    },
                    _ => Geometry with
                    {
                        MinHeight = minHeight,
                        MaxHeight = maxHeight,
                        HeightSamples = HeightSamples,
                    },
                },
            };
        }
    }

    private sealed record DemBoundarySampleKey(
        long QuantizedX,
        long QuantizedZ);

    private sealed record BoundaryHeightSampleReference(
        TerrainGridChunkAlignmentState State,
        int SampleIndex,
        DemBoundarySampleKey Key);

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

        TerrainGridSpatialIndex spatialIndex = TerrainGridSpatialIndex.Create(
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
        double[] localHeights = new double[width * height];
        bool[] sampledInsideTriangles = new bool[width * height];

        for (int zIndex = 0; zIndex < height; zIndex++)
        {
            double v = height == 1 ? 0.0 : (double)zIndex / (height - 1);
            double sampleZ = minZ + (extentZ * v);
            for (int xIndex = 0; xIndex < width; xIndex++)
            {
                double u = width == 1 ? 0.0 : (double)xIndex / (width - 1);
                double sampleX = minX + (extentX * u);
                int sampleIndex = (zIndex * width) + xIndex;
                if (TrySampleLocalDemHeight(sampleX, sampleZ, triangles, spatialIndex, out double localHeight))
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

        ExtendBoundaryConnectedMissingHeightSamples(localHeights, sampledInsideTriangles, width, height);
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
        cancellationToken.ThrowIfCancellationRequested();
        heightMapCityObject = null;

        if (cityObject.Surfaces.SelectMany(static surface => surface.Vertices).Take(3).Count() < 3)
        {
            return false;
        }

        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin = ResolveParsedCityObjectOrigin(cityObject);
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

        Float3 slotPosition = CreateScenePosition(cityObjectOrigin.ToProjectionModel(), globalOriginPoint.ToProjectionModel(), globalCartesian);
        Float3[] positions = cityObject.Surfaces
            .SelectMany(static surface => surface.Vertices)
            .Select(point => CreateGlobalTerrainGridLocalPosition(point.ToProjectionModel(), slotPosition, globalOriginPoint.ToProjectionModel(), globalCartesian))
            .ToArray();
        TerrainGridTriangle[] triangles = CreateDemTerrainGridTriangles(cityObject, slotPosition, globalOriginPoint, globalCartesian);
        double seaLevelLocalHeight = CreateGlobalTerrainGridLocalPosition(
            new GeodeticPoint(cityObjectOrigin.Latitude, cityObjectOrigin.Longitude, 0.0),
            slotPosition,
            globalOriginPoint.ToProjectionModel(),
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

        TerrainGridSpatialIndex spatialIndex = TerrainGridSpatialIndex.Create(
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
                if (TrySampleLocalDemHeight(sampleX, sampleZ, triangles, spatialIndex, out double localHeight))
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
        ExtendBoundaryConnectedMissingHeightSamples(localHeights, sampledInsideTriangles, width, height);
        double minHeight = localHeights.Min();
        double maxHeight = localHeights.Max();

        MaterialBinding[] materials = CityGmlSurfaceMaterialResolver.CreateDemTerrainGridMaterials(
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
            Materials: materials,
            SourceFileRelativePath: cityObject.SourceFileRelativePath);
        return true;
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
        HashSet<string> culledSurfaceIds = GetCulledSurfaceIdsBeforeProjection(
            cityObject.PackageName,
            cityObject.Surfaces,
            cityObjectOrigin,
            cityObjectCartesian);
        double cityObjectMinAltitude = ResolveProjectionMinimumAltitude(cityObject.Surfaces);
        List<ResolvedSurfaceMaterial> resolvedSurfaces =
        [
            .. cityObject.Surfaces
                .Where(surface => !culledSurfaceIds.Contains(surface.PolygonId))
                .Select(surface => ResolveSurfaceMaterial(
                    cityObject,
                    cityObjectOrigin,
                    cityObjectCartesian,
                    surface,
                    cityObjectMinAltitude,
                    demTerrainTextureOverlay,
                    materialResolver)),
        ];

        return resolvedSurfaces
            .GroupBy(
                resolvedSurface => MaterialGroupingPolicy.CreateKey(
                    TerrainOverlayMeshCodeResolver.ResolveMaterialMeshCodeSource(
                        cityObject.ActualMeshCode,
                        requestedMeshCode,
                        requestedMeshCodeBounds,
                        resolvedSurface.Material.TerrainOverlay),
                    resolvedSurface.Material,
                    resolvedSurface.DepthOffset,
                    resolvedSurface.Material.TextureScale,
                    resolvedSurface.Surface.BaseColor,
                    resolvedSurface.Material.TextureOffset))
            .OrderBy(static group => group.Min(static surface => ParsedSurfaceStableSortKey.Create(surface.Surface)), StringComparer.Ordinal)
            .Select((group, materialIndex) =>
            {
                ResolvedSurfaceMaterial representativeSurface = group.First();
                string terrainMaterialMeshCodeSource = TerrainOverlayMeshCodeResolver.ResolveMaterialMeshCodeSource(
                    cityObject.ActualMeshCode,
                    requestedMeshCode,
                    requestedMeshCodeBounds,
                    representativeSurface.Material.TerrainOverlay);
                return CreateMaterialBinding(
                    terrainMaterialMeshCodeSource,
                    representativeSurface,
                    materialIndex);
            })
            .ToArray();
    }

    private static MaterialBinding CreateMaterialBinding(
        string actualMeshCode,
        ResolvedSurfaceMaterial representativeSurface,
        int materialIndex)
    {
        string? terrainMeshCode = representativeSurface.Material.TerrainOverlay is null
            ? null
            : TerrainOverlayMeshCodeResolver.ResolveMeshCode(actualMeshCode, representativeSurface.Material.TerrainOverlay)
                ?? throw CreateTerrainOverlayMeshCodeMismatchException(
                    "material-binding",
                    actualMeshCode,
                    actualMeshCode,
                    requestedMeshCodeBounds: null,
                    representativeSurface.Material.TerrainOverlay);
        ColorRgba baseColor = representativeSurface.Material.TerrainOverlay is null
            ? ToContractColor(representativeSurface.Surface.BaseColor)
            : new ColorRgba(1.0, 1.0, 1.0, 1.0);
        DefaultCommonMaterialMember? commonMaterial = DefaultCommonMaterialAssignment.Resolve(
            baseColor,
            representativeSurface.Material.MaterialType,
            representativeSurface.Material.TexturePayload,
            representativeSurface.Material.TextureSourceKind,
            representativeSurface.Material.Projection,
            representativeSurface.DepthOffset,
            representativeSurface.Material.TextureScale,
            representativeSurface.Material.TextureOffset,
            representativeSurface.Material.TerrainOverlay,
            representativeSurface.Material.CommonMaterial);
        return new MaterialBinding(
            BaseColor: baseColor,
            MaterialType: representativeSurface.Material.MaterialType,
            TexturePayload: representativeSurface.Material.TexturePayload is null
                ? null
                : representativeSurface.Material.TexturePayload,
            TextureSourceKind: representativeSurface.Material.TextureSourceKind,
            Projection: representativeSurface.Material.Projection,
            DepthOffset: representativeSurface.DepthOffset is null
                ? null
                : representativeSurface.DepthOffset,
            SubmeshIndices: [materialIndex],
            TextureScale: representativeSurface.Material.TextureScale is null
                ? null
                : representativeSurface.Material.TextureScale,
            Family: representativeSurface.Material.Family,
            TextureOffset: representativeSurface.Material.TextureOffset is null
                ? null
                : representativeSurface.Material.TextureOffset,
            ReuseScope: representativeSurface.Material.ReuseScope,
            TerrainOverlay: representativeSurface.Material.TerrainOverlay,
            BundledVariantIndex: representativeSurface.Material.BundledVariantIndex,
            TerrainMeshCode: terrainMeshCode,
            CommonMaterial: commonMaterial);
    }

    private static InvalidOperationException CreateTerrainOverlayMeshCodeMismatchException(
        string phase,
        string actualMeshCode,
        string requestedMeshCode,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds,
        TerrainTextureOverlay? terrainOverlay)
    {
        return TerrainOverlayDiagnostics.CreateMeshCodeMismatchException(
            phase,
            actualMeshCode,
            requestedMeshCode,
            requestedMeshCodeBounds,
            terrainOverlay);
    }

    private static Float2 ToContractFloat2(Float2 value) => new(value.X, value.Y);

    private static Float2 ToContractFloat2(ScalarPair value) => new(value.X, value.Y);

    private static Float2 ToInternalFloat2(Float2 value) => new(value.X, value.Y);

    private static ColorRgba ToInternalColor(ColorRgba value) => new(value.R, value.G, value.B, value.A);

    private static Float3 ToContractFloat3(Float3 value) => new(value.X, value.Y, value.Z);

    private static Quaternion ToContractQuaternion(Quaternion value) => new(value.X, value.Y, value.Z, value.W);

    private static ColorRgba ToContractColor(ColorRgba value) => new(value.R, value.G, value.B, value.A);

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
        double cityObjectMinAltitude = ResolveProjectionMinimumAltitude(cityObject.Surfaces);
        ResolvedSurfaceMaterial? representativeSurface = cityObject.Surfaces
            .Select(surface => ResolveSurfaceMaterial(
                cityObject,
                cityObjectOrigin,
                cityObjectCartesian,
                surface,
                cityObjectMinAltitude,
                demTerrainTextureOverlay,
                materialResolver))
            .FirstOrDefault(static resolvedSurface => resolvedSurface.Surface.UsesGeneratedDemTexture);
        if (representativeSurface is null)
        {
            return null;
        }

        TextureUvRect? occupiedUvRect = DemTerrainOverlayAssignment.TryCreateTerrainGridOccupiedUvRect(
            global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.FromProjectionModel(cityObject),
            representativeSurface,
            demTerrainTextureOverlay,
            demObjectBounds);
        return occupiedUvRect is { IsIdentity: true } ? null : occupiedUvRect;
    }

    private static TextureUvRect? TryCreateDemTerrainGridOccupiedUvRect(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        if (demTerrainTextureOverlay is null)
        {
            return null;
        }

        GeographicRectangle? demObjectBounds = TryGetDemObjectGeographicBounds(cityObject, demTerrainTextureOverlay);
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

        TextureUvRect? occupiedUvRect = DemTerrainOverlayAssignment.TryCreateTerrainGridOccupiedUvRect(
            cityObject,
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

    private static GeographicRectangle? TryGetDemObjectGeographicBounds(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        TerrainTextureOverlay? demTerrainTextureOverlay)
    {
        if (demTerrainTextureOverlay is null
            || !string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return ResolveParsedCityObjectGeographicBounds(cityObject);
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

    private static DemTerrainGridBounds CreateDemTerrainGridBounds(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin,
        Float3 slotPosition,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
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
            ResolveParsedCityObjectGeographicBounds(cityObject),
            demTerrainTextureOverlay.GeographicBounds);
        double referenceLatitude = cityObjectOrigin.Latitude;
        double referenceLongitude = cityObjectOrigin.Longitude;
        Float3 westPosition = CreateGlobalTerrainGridLocalPosition(
            new GeodeticPoint(referenceLatitude, clippedBounds.MinLongitude, cityObjectOrigin.Altitude),
            slotPosition,
            globalOriginPoint.ToProjectionModel(),
            globalCartesian);
        Float3 eastPosition = CreateGlobalTerrainGridLocalPosition(
            new GeodeticPoint(referenceLatitude, clippedBounds.MaxLongitude, cityObjectOrigin.Altitude),
            slotPosition,
            globalOriginPoint.ToProjectionModel(),
            globalCartesian);
        Float3 southPosition = CreateGlobalTerrainGridLocalPosition(
            new GeodeticPoint(clippedBounds.MinLatitude, referenceLongitude, cityObjectOrigin.Altitude),
            slotPosition,
            globalOriginPoint.ToProjectionModel(),
            globalCartesian);
        Float3 northPosition = CreateGlobalTerrainGridLocalPosition(
            new GeodeticPoint(clippedBounds.MaxLatitude, referenceLongitude, cityObjectOrigin.Altitude),
            slotPosition,
            globalOriginPoint.ToProjectionModel(),
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

    private static GeographicRectangle ResolveParsedCityObjectGeographicBounds(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject)
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

    private static Float3 TransformPointToWorld(Transform3D transform, Float3 localPosition)
    {
        Float3 rotated = transform.Rotation is null
            ? localPosition
            : Rotate(localPosition, transform.Rotation);
        return Add(transform.Position, rotated);
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

    private static TriangleMeshGeometry RebaseTriangleMeshToTransform(
        TriangleMeshGeometry source,
        Transform3D sourceTransform,
        Transform3D targetTransform)
    {
        ImportedMesh mesh = source.Mesh;
        MeshVertex[] vertices = mesh.Vertices
            .Select(vertex =>
            {
                Float3 worldPosition = TransformPointToWorld(sourceTransform, vertex.Position);
                Float3 localPosition = TransformVectorFromWorld(targetTransform, Subtract(worldPosition, targetTransform.Position));
                Float3 worldNormal = sourceTransform.Rotation is null ? vertex.Normal : Rotate(vertex.Normal, sourceTransform.Rotation);
                Float3 localNormal = TransformVectorFromWorld(targetTransform, worldNormal);
                return vertex with
                {
                    Position = localPosition,
                    Normal = localNormal,
                };
            })
            .ToArray();
        return new TriangleMeshGeometry(new ImportedMesh(vertices, mesh.Submeshes));
    }

    private static Float3 TransformVectorFromWorld(Transform3D transform, Float3 worldVector)
    {
        return transform.Rotation is null
            ? worldVector
            : Rotate(worldVector, Conjugate(transform.Rotation));
    }

    private static Float3 Rotate(Float3 value, Quaternion rotation)
    {
        Float3 qv = new(rotation.X, rotation.Y, rotation.Z);
        Float3 uv = Cross3(qv, value);
        Float3 uuv = Cross3(qv, uv);
        return Add(
            value,
            Add(
                Scale3(uv, 2.0 * rotation.W),
                Scale3(uuv, 2.0)));
    }

    private static Quaternion Conjugate(Quaternion value)
    {
        return new Quaternion(-value.X, -value.Y, -value.Z, value.W);
    }

    private static Float3 Cross3(Float3 left, Float3 right)
    {
        return new Float3(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));
    }

    private static Float3 Scale3(Float3 value, double scalar)
    {
        return new Float3(value.X * scalar, value.Y * scalar, value.Z * scalar);
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

    private static TerrainGridTriangle[] CreateDemTerrainGridTriangles(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        Float3 slotPosition,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian)
    {
        List<TerrainGridTriangle> triangles = [];
        foreach (global::PlateauResoniteLink.Application.Importing.ParsedSurface surface in cityObject.Surfaces)
        {
            Float3[] positions = surface.ExteriorRing.Vertices
                .Select(point => CreateGlobalTerrainGridLocalPosition(point.ToProjectionModel(), slotPosition, globalOriginPoint.ToProjectionModel(), globalCartesian))
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

    private static bool TrySampleLocalDemHeight(
        double x,
        double z,
        IReadOnlyList<TerrainGridTriangle> triangles,
        TerrainGridSpatialIndex spatialIndex,
        out double height)
    {
        foreach (int triangleIndex in spatialIndex.GetCandidateTriangleIndices(x, z))
        {
            TerrainGridTriangle triangle = triangles[triangleIndex];
            if (TryInterpolateLocalTriangleHeight(triangle, x, z, out height))
            {
                return true;
            }
        }

        height = 0.0;
        return false;
    }

    private static bool TryInterpolateLocalTriangleHeight(
        TerrainGridTriangle triangle,
        double x,
        double z,
        out double height)
    {
        double denominator = ((triangle.B.Z - triangle.C.Z) * (triangle.A.X - triangle.C.X))
            + ((triangle.C.X - triangle.B.X) * (triangle.A.Z - triangle.C.Z));
        if (Math.Abs(denominator) < 1e-8)
        {
            height = 0.0;
            return false;
        }

        double weight0 = (((triangle.B.Z - triangle.C.Z) * (x - triangle.C.X))
            + ((triangle.C.X - triangle.B.X) * (z - triangle.C.Z))) / denominator;
        double weight1 = (((triangle.C.Z - triangle.A.Z) * (x - triangle.C.X))
            + ((triangle.A.X - triangle.C.X) * (z - triangle.C.Z))) / denominator;
        double weight2 = 1.0 - weight0 - weight1;
        if (weight0 < -1e-5 || weight1 < -1e-5 || weight2 < -1e-5)
        {
            height = 0.0;
            return false;
        }

        height = (triangle.A.Y * weight0) + (triangle.B.Y * weight1) + (triangle.C.Y * weight2);
        return true;
    }

    private static void ExtendBoundaryConnectedMissingHeightSamples(
        double[] localHeights,
        bool[] sampledInsideTriangles,
        int width,
        int height)
    {
        bool[] boundaryConnectedMissing = FindBoundaryConnectedMissingSamples(sampledInsideTriangles, width, height);
        if (!boundaryConnectedMissing.Any(static missing => missing))
        {
            return;
        }

        Queue<(int Row, int Column)> frontier = new();

        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                int sampleIndex = (row * width) + column;
                if (!sampledInsideTriangles[sampleIndex])
                {
                    continue;
                }

                if (TouchesBoundaryConnectedMissing(row, column))
                {
                    frontier.Enqueue((row, column));
                }
            }
        }

        while (frontier.Count > 0)
        {
            (int row, int column) = frontier.Dequeue();
            int sourceIndex = (row * width) + column;
            TryPropagate(row - 1, column, localHeights[sourceIndex]);
            TryPropagate(row + 1, column, localHeights[sourceIndex]);
            TryPropagate(row, column - 1, localHeights[sourceIndex]);
            TryPropagate(row, column + 1, localHeights[sourceIndex]);
        }

        bool TouchesBoundaryConnectedMissing(int row, int column)
        {
            return IsBoundaryConnectedMissing(row - 1, column)
                || IsBoundaryConnectedMissing(row + 1, column)
                || IsBoundaryConnectedMissing(row, column - 1)
                || IsBoundaryConnectedMissing(row, column + 1);
        }

        bool IsBoundaryConnectedMissing(int row, int column)
        {
            if ((uint)row >= (uint)height || (uint)column >= (uint)width)
            {
                return false;
            }

            return boundaryConnectedMissing[(row * width) + column];
        }

        void TryPropagate(int row, int column, double heightValue)
        {
            if ((uint)row >= (uint)height || (uint)column >= (uint)width)
            {
                return;
            }

            int targetIndex = (row * width) + column;
            if (!boundaryConnectedMissing[targetIndex] || sampledInsideTriangles[targetIndex])
            {
                return;
            }

            localHeights[targetIndex] = heightValue;
            sampledInsideTriangles[targetIndex] = true;
            frontier.Enqueue((row, column));
        }
    }

    private static bool[] FindBoundaryConnectedMissingSamples(
        bool[] sampledInsideTriangles,
        int width,
        int height)
    {
        bool[] boundaryConnectedMissing = new bool[width * height];
        Queue<(int Row, int Column)> frontier = new();

        for (int column = 0; column < width; column++)
        {
            EnqueueIfBoundaryMissing(0, column);
            EnqueueIfBoundaryMissing(height - 1, column);
        }

        for (int row = 1; row < height - 1; row++)
        {
            EnqueueIfBoundaryMissing(row, 0);
            EnqueueIfBoundaryMissing(row, width - 1);
        }

        while (frontier.Count > 0)
        {
            (int row, int column) = frontier.Dequeue();
            TryVisit(row - 1, column);
            TryVisit(row + 1, column);
            TryVisit(row, column - 1);
            TryVisit(row, column + 1);
        }

        return boundaryConnectedMissing;

        void EnqueueIfBoundaryMissing(int row, int column)
        {
            if ((uint)row >= (uint)height || (uint)column >= (uint)width)
            {
                return;
            }

            int sampleIndex = (row * width) + column;
            if (sampledInsideTriangles[sampleIndex] || boundaryConnectedMissing[sampleIndex])
            {
                return;
            }

            boundaryConnectedMissing[sampleIndex] = true;
            frontier.Enqueue((row, column));
        }

        void TryVisit(int row, int column)
        {
            if ((uint)row >= (uint)height || (uint)column >= (uint)width)
            {
                return;
            }

            int sampleIndex = (row * width) + column;
            if (sampledInsideTriangles[sampleIndex] || boundaryConnectedMissing[sampleIndex])
            {
                return;
            }

            boundaryConnectedMissing[sampleIndex] = true;
            frontier.Enqueue((row, column));
        }
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

    internal sealed record SourceFileDescriptor(
        string RelativePath,
        string PackageName,
        string MatchedMeshCode,
        bool RequiresMeshCodeBoundsFilter);

    internal sealed record CachedSourceFileDescriptor(
        SourceFileDescriptor SourceFile,
        ParsedCityObject[] CityObjects)
    {
        public string RelativePath => SourceFile.RelativePath;

        public string PackageName => SourceFile.PackageName;
    }

    internal sealed class SourceFilePipeline
    {
        private readonly object parseTaskGate = new();
        private readonly Func<Task<ParsedSourceFileResult>> parseTaskFactory;
        private Task<ParsedSourceFileResult>? parseTask;

        public SourceFilePipeline(
            SourceFileDescriptor sourceFile,
            Func<Task<ParsedSourceFileResult>> parseTaskFactory)
        {
            SourceFile = sourceFile;
            this.parseTaskFactory = parseTaskFactory;
        }

        public SourceFileDescriptor SourceFile { get; }

        public Task<ParsedSourceFileResult> GetParseTask()
        {
            lock (parseTaskGate)
            {
                parseTask ??= parseTaskFactory();
                return parseTask;
            }
        }
    }

    internal sealed record ParsedSourceFileResult(
        SourceFileDescriptor SourceFile,
        ParsedCityObject[] CityObjects,
        CoordinateReferenceSystem? ReferenceSystem,
        ProjectionTerrainHeightTriangle[] TerrainTriangles,
        TimeSpan Elapsed);

    internal sealed record ParsedRing(
        string RingId,
        GeodeticPoint[] Vertices,
        IReadOnlyList<Float2>? UVs);

    private sealed record TerrainGridTriangle(
        Float3 A,
        Float3 B,
        Float3 C);

    private sealed record DemTerrainGridBounds(
        double MinX,
        double MaxX,
        double MinZ,
        double MaxZ);

    private sealed class TerrainGridSpatialIndex
    {
        private static readonly IReadOnlyList<int> EmptyTriangleIndices = Array.Empty<int>();

        private readonly IReadOnlyList<int>[] triangleBuckets;
        private readonly double minX;
        private readonly double minZ;
        private readonly double inverseCellSizeX;
        private readonly double inverseCellSizeZ;
        private readonly int cellsX;
        private readonly int cellsZ;

        private TerrainGridSpatialIndex(
            IReadOnlyList<int>[] triangleBuckets,
            double minX,
            double minZ,
            double inverseCellSizeX,
            double inverseCellSizeZ,
            int cellsX,
            int cellsZ)
        {
            this.triangleBuckets = triangleBuckets;
            this.minX = minX;
            this.minZ = minZ;
            this.inverseCellSizeX = inverseCellSizeX;
            this.inverseCellSizeZ = inverseCellSizeZ;
            this.cellsX = cellsX;
            this.cellsZ = cellsZ;
        }

        public static TerrainGridSpatialIndex Create(
            IReadOnlyList<TerrainGridTriangle> triangles,
            double minX,
            double maxX,
            double minZ,
            double maxZ)
        {
            if (triangles.Count == 0)
            {
                return new TerrainGridSpatialIndex([EmptyTriangleIndices], minX, minZ, 1.0, 1.0, 1, 1);
            }

            double extentX = Math.Max(maxX - minX, 1e-6);
            double extentZ = Math.Max(maxZ - minZ, 1e-6);
            double aspectRatio = extentX / extentZ;
            double baseCellCount = Math.Ceiling(Math.Sqrt(triangles.Count));
            int cellsX = Math.Clamp((int)Math.Ceiling(baseCellCount * Math.Sqrt(aspectRatio)), 1, 256);
            int cellsZ = Math.Clamp((int)Math.Ceiling(baseCellCount / Math.Sqrt(aspectRatio)), 1, 256);
            double cellSizeX = extentX / cellsX;
            double cellSizeZ = extentZ / cellsZ;
            List<int>[] mutableTriangleBuckets = new List<int>[cellsX * cellsZ];

            for (int triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
            {
                TerrainGridTriangle triangle = triangles[triangleIndex];
                double triangleMinX = Math.Min(triangle.A.X, Math.Min(triangle.B.X, triangle.C.X));
                double triangleMaxX = Math.Max(triangle.A.X, Math.Max(triangle.B.X, triangle.C.X));
                double triangleMinZ = Math.Min(triangle.A.Z, Math.Min(triangle.B.Z, triangle.C.Z));
                double triangleMaxZ = Math.Max(triangle.A.Z, Math.Max(triangle.B.Z, triangle.C.Z));
                int startX = GetCellIndex(triangleMinX, minX, cellSizeX, cellsX);
                int endX = GetCellIndex(triangleMaxX, minX, cellSizeX, cellsX);
                int startZ = GetCellIndex(triangleMinZ, minZ, cellSizeZ, cellsZ);
                int endZ = GetCellIndex(triangleMaxZ, minZ, cellSizeZ, cellsZ);

                for (int cellZ = startZ; cellZ <= endZ; cellZ++)
                {
                    for (int cellX = startX; cellX <= endX; cellX++)
                    {
                        int bucketIndex = (cellZ * cellsX) + cellX;
                        (mutableTriangleBuckets[bucketIndex] ??= []).Add(triangleIndex);
                    }
                }
            }

            IReadOnlyList<int>[] triangleBuckets = new IReadOnlyList<int>[mutableTriangleBuckets.Length];
            for (int bucketIndex = 0; bucketIndex < triangleBuckets.Length; bucketIndex++)
            {
                triangleBuckets[bucketIndex] = mutableTriangleBuckets[bucketIndex]?.ToArray()
                    ?? EmptyTriangleIndices;
            }

            return new TerrainGridSpatialIndex(
                triangleBuckets,
                minX,
                minZ,
                1.0 / cellSizeX,
                1.0 / cellSizeZ,
                cellsX,
                cellsZ);
        }

        public IReadOnlyList<int> GetCandidateTriangleIndices(double x, double z)
        {
            int cellX = Math.Clamp((int)((x - minX) * inverseCellSizeX), 0, cellsX - 1);
            int cellZ = Math.Clamp((int)((z - minZ) * inverseCellSizeZ), 0, cellsZ - 1);
            IReadOnlyList<int> bucket = triangleBuckets[(cellZ * cellsX) + cellX];
            return bucket is { Count: > 0 } ? bucket : EmptyTriangleIndices;
        }

        private static int GetCellIndex(double coordinate, double minimum, double cellSize, int cellCount)
        {
            return Math.Clamp((int)((coordinate - minimum) / cellSize), 0, cellCount - 1);
        }
    }

    private readonly record struct TerrainSampleAnchor(
        double Latitude,
        double Longitude,
        double Altitude);

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

    private sealed record TessellatedVertex(
        Float3 Position,
        Float2 UV,
        ColorRgba? Color);

    private sealed record TessellatedRing(
        string RingId,
        IReadOnlyList<TessellatedVertex> Vertices);

    private sealed record TessVertexPayload(
        Float3 Position,
        Float2 UV,
        ColorRgba? Color);

    private sealed record SurfaceUvAxes(
        Float3 AxisU,
        Float3 AxisV);

    private sealed record SurfaceUvProjection(
        Float3 AxisU,
        Float3 AxisV,
        double OffsetV);

    private readonly record struct FacadeUvProjectionContext(
        double MinimumY,
        double MaximumY,
        double FloorHeightMeters,
        int FloorCount);

    private readonly record struct DemUvProjection(
        double West,
        double South,
        double Width,
        double Height);

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
