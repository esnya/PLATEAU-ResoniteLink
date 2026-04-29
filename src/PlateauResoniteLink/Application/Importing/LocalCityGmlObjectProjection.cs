using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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

internal static partial class LocalCityGmlObjectProjection
{
    private const double BuildingBottomCullBandMeters = 0.1;
    private const double UnknownRoofBottomAltitudeToleranceMeters = 0.1;
    public const string DefaultDemTerrainTexturePath = DemTerrainTextureDefaults.PlateauOrthoPath;
    public const string DefaultDemTerrainTextureUrlTemplate = DemTerrainTextureDefaults.PlateauOrthoUrlTemplate;
    public const string DefaultDemTerrainTextureFallbackUrlTemplate = DemTerrainTextureDefaults.GsiFallbackUrlTemplate;
    public const int DefaultDemTerrainTextureZoomLevel = DemTerrainTextureDefaults.PlateauOrthoZoomLevel;
    public const int DefaultDemTerrainTextureFallbackZoomLevel = DemTerrainTextureDefaults.FallbackZoomLevel;
    public const int DefaultDemTerrainTextureMaxSize = DemTerrainTextureDefaults.MaxTextureSize;
    public const double DefaultGeneratedRoadMarkingWidthMeters = 0.15;
    public const double DefaultGeneratedRoadMarkingSegmentLengthMeters = 5.0;
    public const double DefaultTerrainAlignedTransportationSegmentLengthMeters = 5.0;
    public const double MinTerrainAlignedTransportationSegmentLengthMeters = 2.0;
    public const double TerrainAlignedTransportationSegmentLengthByWidthRatio = 0.8;
    public static readonly MaterialDepthOffset DefaultTerrainAlignedMaterialDepthOffset = new(-10.0, -10.0);

    private static readonly Quaternion GridMeshTerrainRotation = new(
        X: Math.Sqrt(0.5),
        Y: 0.0,
        Z: 0.0,
        W: Math.Sqrt(0.5));
    private static readonly ColorRgba DefaultMaterialColor = new(1.0, 1.0, 1.0, 1.0);
    private static readonly ColorRgba DefaultVegetationMaterialColor = new(0.32, 0.58, 0.24, 1.0);
    private static readonly ColorRgba DefaultRoadMarkingColor = new(1.0, 1.0, 1.0, 1.0);
    private static readonly XNamespace App = "http://www.opengis.net/citygml/appearance/2.0";
    private static readonly XNamespace Core = "http://www.opengis.net/citygml/2.0";
    private static readonly XNamespace Gml = "http://www.opengis.net/gml";
    private static readonly Regex ConcreteMeshCodeTokenRegex = new(
        @"(?<!\d)(\d{8})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);



    private static ParsedCityObject[] ParseCityObjects(
        XElement cityObjectElement,
        string packageName,
        string relativeSourceFile,
        string actualMeshCode,
        bool sharedAcrossMeshCodes,
        ICityGmlAppearanceStore appearanceStore,
        ICityGmlSourceRepresentationSelector sourceRepresentationSelector,
        CoordinateReferenceSystem coordinateReferenceSystem,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas,
        LodFilteringStrategy lodFilteringStrategy)
    {
        string objectTypeName = cityObjectElement.Name.LocalName;
        string objectId = GetAttribute(cityObjectElement, Gml + "id") ?? objectTypeName;
        string? displayName = cityObjectElement.Elements(Gml + "name").FirstOrDefault()?.Value.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = objectId;
        }

        string resolvedActualMeshCode =
            sharedAcrossMeshCodes && string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase)
                ? ResolveConcreteActualMeshCode(displayName!, objectId, actualMeshCode)
                : actualMeshCode;
        int? floorsAboveGround = TryParseStoreysAboveGround(cityObjectElement);
        double? measuredHeightMeters = TryParseMeasuredHeightMeters(cityObjectElement);

        bool isMarking = displayName.Contains("Marking", StringComparison.OrdinalIgnoreCase)
            || objectId.Contains("Marking", StringComparison.OrdinalIgnoreCase)
            || objectId.Contains("_road_marking", StringComparison.Ordinal);

        CityGmlSourceRepresentationSelection[] sourceRepresentationSelections = sourceRepresentationSelector.SelectSurfaceRepresentations(
            cityObjectElement,
            packageName,
            isMarking,
            lodFilteringStrategy);

        if (!lodFilteringStrategy.ShouldIncludeByPattern(packageName, objectId, isMarking))
        {
            return [];
        }

        string fileStem = Path.GetFileNameWithoutExtension(relativeSourceFile);
        string slotKey = SanitizeIdentifier($"{packageName}_{fileStem}_{objectId}");
        return sourceRepresentationSelections
            .Select(sourceRepresentationSelection => CreateParsedCityObjectForSourceRepresentation(
                sourceRepresentationSelection,
                packageName,
                displayName!,
                resolvedActualMeshCode,
                slotKey,
                coordinateReferenceSystem,
                relativeSourceFile,
                sharedAcrossMeshCodes,
                requestedMeshAreas,
                floorsAboveGround,
                measuredHeightMeters,
                appearanceStore))
            .Where(static cityObject => cityObject is not null)
            .Select(static cityObject => cityObject!)
            .ToArray();
    }

    private static ParsedCityObject? CreateParsedCityObjectForSourceRepresentation(
        CityGmlSourceRepresentationSelection sourceRepresentationSelection,
        string packageName,
        string displayName,
        string resolvedActualMeshCode,
        string slotKey,
        CoordinateReferenceSystem coordinateReferenceSystem,
        string relativeSourceFile,
        bool sharedAcrossMeshCodes,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas,
        int? floorsAboveGround,
        double? measuredHeightMeters,
        ICityGmlAppearanceStore appearanceStore)
    {
        ParsedSurface[] surfaces = sourceRepresentationSelection.SurfaceElements
            .Select(surfaceElement => ParseSurface(surfaceElement, appearanceStore))
            .Where(static surface => surface is not null)
            .Select(static surface => surface!)
            .Select(surface => ApplyPackageSurfaceDefaults(packageName, surface))
            .OrderBy(static surface => CreateStableSurfaceSortKey(surface), StringComparer.Ordinal)
            .ToArray();

        if (surfaces.Length == 0)
        {
            return null;
        }

        if (requestedMeshAreas is not null
            && requestedMeshAreas.Count > 0
            && coordinateReferenceSystem.IsGeographic)
        {
            bool intersectsRequestedMeshArea = sharedAcrossMeshCodes
                && TryCreateMeshCodeBounds(resolvedActualMeshCode, out MeshCodeBounds? resolvedActualMeshArea)
                    ? IntersectsMeshCodeBounds(resolvedActualMeshArea!, requestedMeshAreas)
                    : IntersectsMeshCodeBounds(surfaces, requestedMeshAreas);
            if (!intersectsRequestedMeshArea)
            {
                return null;
            }
        }

        return new ParsedCityObject(
            slotKey,
            displayName,
            packageName,
            resolvedActualMeshCode,
            sourceRepresentationSelection.DetailEntry,
            surfaces,
            coordinateReferenceSystem,
            relativeSourceFile,
            SharedAcrossMeshCodes: sharedAcrossMeshCodes,
            FloorsAboveGround: floorsAboveGround,
            MeasuredHeightMeters: measuredHeightMeters);
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

    private static ParsedSurface ApplyPackageSurfaceDefaults(string packageName, ParsedSurface surface)
    {
        return string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase)
            && surface.TexturePayload is null
            ? surface with { UsesGeneratedDemTexture = true }
            : surface;
    }

    private static bool IntersectsMeshCodeBounds(
        IEnumerable<ParsedSurface> surfaces,
        IReadOnlyList<MeshCodeBounds> meshCodeAreas)
    {
        List<GeodeticPoint> vertices = surfaces
            .SelectMany(static surface => surface.Vertices)
            .ToList();

        double minLatitude = vertices.Min(static point => point.Latitude);
        double maxLatitude = vertices.Max(static point => point.Latitude);
        double minLongitude = vertices.Min(static point => point.Longitude);
        double maxLongitude = vertices.Max(static point => point.Longitude);

        return IntersectsMeshCodeBounds(minLatitude, maxLatitude, minLongitude, maxLongitude, meshCodeAreas);
    }

    private static bool IntersectsMeshCodeBounds(
        MeshCodeBounds meshCodeArea,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas)
    {
        return IntersectsMeshCodeBounds(
            meshCodeArea.SouthLatitude,
            meshCodeArea.NorthLatitude,
            meshCodeArea.WestLongitude,
            meshCodeArea.EastLongitude,
            requestedMeshAreas);
    }

    private static bool IntersectsMeshCodeBounds(
        double minLatitude,
        double maxLatitude,
        double minLongitude,
        double maxLongitude,
        IReadOnlyList<MeshCodeBounds> meshCodeAreas)
    {
        const double overlapTolerance = 1e-10;

        return meshCodeAreas.Any(meshCodeArea =>
        {
            double latitudeOverlap = Math.Min(maxLatitude, meshCodeArea.NorthLatitude)
                - Math.Max(minLatitude, meshCodeArea.SouthLatitude);
            if (latitudeOverlap <= overlapTolerance)
            {
                return false;
            }

            double longitudeOverlap = Math.Min(maxLongitude, meshCodeArea.EastLongitude)
                - Math.Max(minLongitude, meshCodeArea.WestLongitude);
            return longitudeOverlap > overlapTolerance;
        });
    }

    private static string ResolveConcreteActualMeshCode(
        string displayName,
        string objectId,
        string fallbackActualMeshCode)
    {
        return TryResolveConcreteMeshCode(displayName, out string? displayNameMeshCode)
            ? displayNameMeshCode!
            : TryResolveConcreteMeshCode(objectId, out string? objectIdMeshCode)
                ? objectIdMeshCode!
                : fallbackActualMeshCode;
    }

    private static bool TryResolveConcreteMeshCode(string value, out string? meshCode)
    {
        meshCode = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        Match match = ConcreteMeshCodeTokenRegex.Match(value);
        if (!match.Success)
        {
            return false;
        }

        string candidate = match.Groups[1].Value;
        if (!PlateauMeshCode.TryGetBounds(candidate, out _))
        {
            return false;
        }

        meshCode = candidate;
        return true;
    }

    private static bool TryCreateMeshCodeBounds(string meshCode, out MeshCodeBounds? meshCodeArea)
    {
        meshCodeArea = MeshCodeBounds.TryParse(meshCode);
        return meshCodeArea is not null;
    }

    private static int? TryParseStoreysAboveGround(XElement cityObjectElement)
    {
        XElement? element = cityObjectElement.Elements()
            .FirstOrDefault(static child => string.Equals(child.Name.LocalName, "storeysAboveGround", StringComparison.Ordinal));
        if (element is null)
        {
            return null;
        }

        return int.TryParse(element.Value.Trim(), CultureInfo.InvariantCulture, out int value)
            && (value == 0 || FacadeFloorMetrics.IsUsableFloorCount(value))
            ? value
            : null;
    }

    private static double? TryParseMeasuredHeightMeters(XElement cityObjectElement)
    {
        XElement? element = cityObjectElement.Elements()
            .FirstOrDefault(static child => string.Equals(child.Name.LocalName, "measuredHeight", StringComparison.Ordinal));
        if (element is null)
        {
            return null;
        }

        string? unitOfMeasure = element.Attribute("uom")?.Value.Trim();
        if (!string.IsNullOrWhiteSpace(unitOfMeasure)
            && !string.Equals(unitOfMeasure, "m", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return double.TryParse(element.Value.Trim(), CultureInfo.InvariantCulture, out double value) && value > 0.0
            ? value
            : null;
    }

    private static string CreateStableElementId(string prefix, XElement element)
    {
        byte[] payload = Encoding.UTF8.GetBytes(element.ToString(SaveOptions.DisableFormatting));
        byte[] hash = SHA256.HashData(payload);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}_{Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant()}");
    }

    private static string CreateStableSurfaceSortKey(ParsedSurface surface)
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(surface.PolygonId);
            writer.Write((int)surface.Semantic);
            WriteRing(writer, surface.ExteriorRing);
            writer.Write(surface.InteriorRings.Length);
            foreach (ParsedRing ring in surface.InteriorRings.OrderBy(static ring => ring.RingId, StringComparer.Ordinal))
            {
                WriteRing(writer, ring);
            }
        }

        byte[] hash = SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();

        static void WriteRing(BinaryWriter writer, ParsedRing ring)
        {
            writer.Write(ring.RingId);
            writer.Write(ring.Vertices.Length);
            foreach (GeodeticPoint vertex in ring.Vertices)
            {
                writer.Write(vertex.Latitude);
                writer.Write(vertex.Longitude);
                writer.Write(vertex.Altitude);
            }

            IReadOnlyList<Float2>? uvs = ring.UVs;
            writer.Write(uvs?.Count ?? -1);
            if (uvs is null)
            {
                return;
            }

            foreach (Float2 uv in uvs)
            {
                writer.Write(uv.X);
                writer.Write(uv.Y);
            }
        }
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
            demParsedSourceFiles.Select(global::PlateauResoniteLink.Application.Importing.ParsedSourceFileResult.FromProjectionModel),
            fallbackBounds is null ? null : DemTerrainBounds.FromProjectionModel(fallbackBounds));
        return bounds?.ToProjectionModel();
    }

    private static TerrainHeightTriangle[] ExtractTerrainHeightTriangles(
        IEnumerable<ParsedCityObject> cityObjects)
    {
        return DemSourceDiscoverySupport.CreateTerrainHeightTriangles(
                cityObjects.Select(global::PlateauResoniteLink.Application.Importing.ParsedCityObject.FromProjectionModel))
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

        GeodeticPoint cityObjectOrigin = GetCityObjectOrigin(cityObject);
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

        EdgePairSelection edgePair = SelectPrimaryRoadEdgePair(surface.ExteriorRing, positions);
        double segmentLength = ComputeTerrainAlignedSegmentLength(edgePair.Width);
        if (edgePair.Length <= segmentLength + 1e-6)
        {
            return [surface];
        }

        List<ParsedSurface> strips = CreateTerrainAlignedTransportationStrips(surface, positions, edgePair, segmentLength);
        return strips.Count > 0 ? strips : [surface];
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

        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin = GetCityObjectOrigin(cityObject);
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

        EdgePairSelection edgePair = SelectPrimaryRoadEdgePair(surface.ExteriorRing.ToProjectionModel(), positions);
        double segmentLength = ComputeTerrainAlignedSegmentLength(edgePair.Width);
        if (edgePair.Length <= segmentLength + 1e-6)
        {
            return [surface];
        }

        List<ParsedSurface> strips = CreateTerrainAlignedTransportationStrips(surface.ToProjectionModel(), positions, edgePair, segmentLength);
        return strips.Count > 0
            ? strips.Select(global::PlateauResoniteLink.Application.Importing.ParsedSurface.FromProjectionModel).ToList()
            : [surface];
    }

    private static double ComputeTerrainAlignedSegmentLength(double roadWidth)
    {
        double preferredLength = roadWidth * TerrainAlignedTransportationSegmentLengthByWidthRatio;
        return Math.Clamp(
            preferredLength,
            MinTerrainAlignedTransportationSegmentLengthMeters,
            DefaultTerrainAlignedTransportationSegmentLengthMeters);
    }

    private static List<ParsedSurface> CreateTerrainAlignedTransportationStrips(
        ParsedSurface surface,
        Float3[] positions,
        EdgePairSelection edgePair,
        double segmentLength)
    {
        Float3 axis = CreateTransportationSurfaceAxis(edgePair);
        if (LengthSquared(axis) < 1e-8)
        {
            return [];
        }

        double minStation = positions.Min(position => DotHorizontal(position, axis));
        double maxStation = positions.Max(position => DotHorizontal(position, axis));
        if (maxStation - minStation <= 1e-6)
        {
            return [];
        }

        SortedSet<double> stations = [minStation, maxStation];
        foreach (Float3 position in positions)
        {
            stations.Add(DotHorizontal(position, axis));
        }

        for (double station = minStation + segmentLength; station < maxStation - 1e-6; station += segmentLength)
        {
            stations.Add(station);
        }

        List<(double Station, SurfaceSliceSample[] Samples)> slices = new(stations.Count);
        foreach (double station in stations)
        {
            SurfaceSliceSample[] samples = IntersectTransportationSurfaceAtStation(surface.ExteriorRing, positions, axis, station);
            if (samples.Length > 0)
            {
                slices.Add((station, samples));
            }
        }

        List<ParsedSurface> strips = [];
        for (int index = 1; index < slices.Count; index++)
        {
            SurfaceSliceSample[] previousSamples = slices[index - 1].Samples;
            SurfaceSliceSample[] currentSamples = slices[index].Samples;
            if (previousSamples.Length == 2 && currentSamples.Length == 2)
            {
                strips.Add(CreateTransportationStripSurface(
                    surface,
                    $"terrain_strip_{index - 1:D2}",
                    previousSamples[0],
                    previousSamples[1],
                    currentSamples[1],
                    currentSamples[0]));
            }
            else if (previousSamples.Length == 1 && currentSamples.Length == 2)
            {
                strips.Add(CreateTransportationStripSurface(
                    surface,
                    $"terrain_fan_start_{index - 1:D2}",
                    previousSamples[0],
                    currentSamples[1],
                    currentSamples[0]));
            }
            else if (previousSamples.Length == 2 && currentSamples.Length == 1)
            {
                strips.Add(CreateTransportationStripSurface(
                    surface,
                    $"terrain_fan_end_{index - 1:D2}",
                    previousSamples[0],
                    previousSamples[1],
                    currentSamples[0]));
            }
        }

        return strips;
    }

    private static ParsedSurface CreateTransportationStripSurface(
        ParsedSurface sourceSurface,
        string suffix,
        params SurfaceSliceSample[] samples)
    {
        Float2[]? uvs = null;
        if (samples.All(static sample => sample.UV is not null))
        {
            List<Float2> uvList = new(samples.Length);
            for (int index = 0; index < samples.Length; index++)
            {
                if (samples[index].UV is Float2 uv)
                {
                    uvList.Add(uv);
                }
            }

            uvs = [.. uvList];
        }

        return sourceSurface with
        {
            PolygonId = $"{sourceSurface.PolygonId}_{suffix}",
            ExteriorRing = new ParsedRing(
                $"{sourceSurface.ExteriorRing.RingId}_{suffix}",
                [.. samples.Select(static sample => sample.Point)],
                uvs),
        };
    }

    private static SurfaceSliceSample[] IntersectTransportationSurfaceAtStation(
        ParsedRing ring,
        Float3[] positions,
        Float3 axis,
        double station)
    {
        Float3 lateralAxis = new(-axis.Z, 0.0, axis.X);
        List<SurfaceSliceSample> intersections = [];
        for (int index = 0; index < ring.Vertices.Length; index++)
        {
            int nextIndex = (index + 1) % ring.Vertices.Length;
            double startStation = DotHorizontal(positions[index], axis);
            double endStation = DotHorizontal(positions[nextIndex], axis);
            double deltaStation = endStation - startStation;
            if (Math.Abs(deltaStation) < 1e-8)
            {
                if (Math.Abs(station - startStation) > 1e-8)
                {
                    continue;
                }

                TryAddSurfaceSliceSample(intersections, ring, positions, lateralAxis, index, ring.Vertices[index], 0.0);
                TryAddSurfaceSliceSample(intersections, ring, positions, lateralAxis, nextIndex, ring.Vertices[nextIndex], 0.0);
                continue;
            }

            double ratio = (station - startStation) / deltaStation;
            if (ratio < -1e-8 || ratio > 1.0 + 1e-8)
            {
                continue;
            }

            ratio = Math.Clamp(ratio, 0.0, 1.0);
            GeodeticPoint point = InterpolateAlongEdge(ring.Vertices[index], ring.Vertices[nextIndex], ratio);
            TryAddSurfaceSliceSample(intersections, ring, positions, lateralAxis, index, point, ratio);
        }

        intersections.Sort(static (left, right) => left.LateralPosition.CompareTo(right.LateralPosition));
        return [.. intersections];
    }

    private static void TryAddSurfaceSliceSample(
        List<SurfaceSliceSample> intersections,
        ParsedRing ring,
        Float3[] positions,
        Float3 lateralAxis,
        int edgeStartIndex,
        GeodeticPoint point,
        double ratio)
    {
        if (intersections.Any(existing => AreSamePoint(existing.Point, point)))
        {
            return;
        }

        int edgeEndIndex = (edgeStartIndex + 1) % ring.Vertices.Length;
        Float3 position = Lerp(positions[edgeStartIndex], positions[edgeEndIndex], ratio);
        Float2? uv = ring.UVs is not null && ring.UVs.Count == ring.Vertices.Length
            ? Lerp(ring.UVs[edgeStartIndex], ring.UVs[edgeEndIndex], ratio)
            : null;
        intersections.Add(new SurfaceSliceSample(point, uv, DotHorizontal(position, lateralAxis)));
    }

    private static Float3 CreateTransportationSurfaceAxis(EdgePairSelection edgePair)
    {
        Float3 side0Vector = NormalizeHorizontal(Subtract(edgePair.Side0Positions[1], edgePair.Side0Positions[0]));
        Float3 side1Vector = NormalizeHorizontal(Subtract(edgePair.Side1Positions[1], edgePair.Side1Positions[0]));
        if (LengthSquared(side0Vector) < 1e-8)
        {
            return side1Vector;
        }

        if (LengthSquared(side1Vector) < 1e-8)
        {
            return side0Vector;
        }

        if (DotHorizontal(side0Vector, side1Vector) < 0.0)
        {
            side1Vector = new Float3(-side1Vector.X, 0.0, -side1Vector.Z);
        }

        return NormalizeHorizontal(Add(side0Vector, side1Vector));
    }

    private static Float3 Add(Float3 left, Float3 right)
    {
        return new Float3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
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
        TerrainHeightSampler terrainHeightSampler,
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
        TerrainHeightSampler terrainHeightSampler,
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

    private static ParsedSurface ConformSurfaceToTerrain(
        ParsedSurface surface,
        TerrainHeightSampler terrainHeightSampler,
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
        return ShouldTerrainAlignCityObject(cityObject.PackageName, cityObject.DetailEntry);
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

    private static ParsedSurface? ParseSurface(XElement polygonElement, ICityGmlAppearanceStore appearanceStore)
    {
        XElement? exteriorRing = polygonElement
            .Element(Gml + "exterior")
            ?.Element(Gml + "LinearRing");
        if (exteriorRing is null)
        {
            return null;
        }

        string polygonId = GetAttribute(polygonElement, Gml + "id") ?? CreateStableElementId("polygon", polygonElement);
        CityGmlResolvedAppearance appearance = appearanceStore.Resolve(polygonId);
        ParsedRing? exteriorParsedRing = ParseRing(
            exteriorRing,
            appearance.RingUvsByRingId,
            fallbackRingId: polygonId);
        if (exteriorParsedRing is null)
        {
            return null;
        }

        ParsedRing[] interiorRings = polygonElement
            .Elements(Gml + "interior")
            .Select(interiorElement => ParseRing(
                interiorElement.Element(Gml + "LinearRing"),
                appearance.RingUvsByRingId,
                fallbackRingId: null))
            .Where(static ring => ring is not null)
            .Select(static ring => ring!)
            .ToArray();

        return new ParsedSurface(
            PolygonId: polygonId,
            Semantic: ParseSurfaceSemantic(polygonElement),
            ExteriorRing: exteriorParsedRing,
            InteriorRings: interiorRings,
            BaseColor: ToInternalColor(appearance.BaseColor),
            TexturePayload: appearance.TexturePayload,
            OpticalProperties: CreateMaterialOpticalProperties(appearance.MaterialAttributes));
    }

    private static ParsedRing? ParseRing(
        XElement? ringElement,
        IReadOnlyDictionary<string, IReadOnlyList<Float2>>? ringUvsByRingId,
        string? fallbackRingId)
    {
        if (ringElement is null)
        {
            return null;
        }

        string ringId = GetAttribute(ringElement, Gml + "id")
            ?? fallbackRingId
            ?? CreateStableElementId("ring", ringElement);
        GeodeticPoint[] vertices = ParseRingPoints(ringElement);
        if (vertices.Length < 3)
        {
            return null;
        }

        IReadOnlyList<Float2>? uvs = null;
        if (ringUvsByRingId is not null
            && ringUvsByRingId.TryGetValue(ringId, out IReadOnlyList<Float2>? ringUvs)
            && ringUvs.Count == vertices.Length)
        {
            uvs = ringUvs;
        }

        return new ParsedRing(ringId, vertices, uvs);
    }

    private static GeodeticPoint[] ParseRingPoints(XElement ringElement)
    {
        List<double> ordinates = [];
        XElement? posListElement = ringElement.Element(Gml + "posList");
        if (posListElement is not null)
        {
            ordinates.AddRange(ParseDoubles(posListElement.Value));
        }
        else
        {
            foreach (XElement posElement in ringElement.Elements(Gml + "pos"))
            {
                ordinates.AddRange(ParseDoubles(posElement.Value));
            }
        }

        List<GeodeticPoint> points = [];
        for (int index = 0; index + 2 < ordinates.Count; index += 3)
        {
            points.Add(new GeodeticPoint(ordinates[index], ordinates[index + 1], ordinates[index + 2]));
        }

        if (points.Count > 1 && AreSamePoint(points[0], points[^1]))
        {
            points.RemoveAt(points.Count - 1);
        }

        return points.ToArray();
    }

    private static GeodeticPoint ComputeGlobalOrigin(IEnumerable<ParsedCityObject> cityObjects)
    {
        return CreateGlobalOrigin(GetBounds(cityObjects), requestedMeshArea: null, isGeographicReferenceSystem: false);
    }

    private static GeodeticPoint CreateGlobalOrigin(
        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude) bounds,
        MeshCodeBounds? requestedMeshArea,
        bool isGeographicReferenceSystem)
    {
        if (isGeographicReferenceSystem && requestedMeshArea is not null)
        {
            return new GeodeticPoint(
                Latitude: (requestedMeshArea.SouthLatitude + requestedMeshArea.NorthLatitude) / 2.0,
                Longitude: (requestedMeshArea.WestLongitude + requestedMeshArea.EastLongitude) / 2.0,
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
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin = GetCityObjectOrigin(cityObject);

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
        HashSet<string> culledSurfaceIds = GetCulledSurfaceIdsBeforeProjection(
            cityObject.PackageName,
            cityObject.Surfaces.Select(static surface => surface.ToProjectionModel()),
            cityObjectOrigin.ToProjectionModel(),
            cityObjectCartesian);
        List<MeshVertex> vertices = [];
        List<MeshSubmesh> submeshes = [];
        List<MaterialBinding> materials = [];
        DemUvProjection? demUvProjection = TryCreateDemUvProjection(cityObject.ActualMeshCode, demTerrainTextureOverlay);
        double cityObjectMinAltitude = cityObject.Surfaces
            .SelectMany(static surface => surface.Vertices)
            .Min(static vertex => vertex.Altitude);

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

        IGrouping<MaterialGroupingKey, ResolvedSurfaceMaterial>[] materialGroups = resolvedSurfaces
            .GroupBy(
                resolvedSurface => CreateMaterialGroupingKey(
                    cityObject.ActualMeshCode,
                    resolvedSurface.Material,
                    resolvedSurface.DepthOffset,
                    resolvedSurface.Material.TextureScale,
                    resolvedSurface.Surface.BaseColor,
                    resolvedSurface.Material.TextureOffset))
            .OrderBy(static group => group.Min(static surface => CreateStableSurfaceSortKey(surface.Surface)), StringComparer.Ordinal)
            .ToArray();

        for (int materialIndex = 0; materialIndex < materialGroups.Length; materialIndex++)
        {
            IGrouping<MaterialGroupingKey, ResolvedSurfaceMaterial> materialGroup = materialGroups[materialIndex];
            List<int> indices = [];
            FacadeUvProjectionContext? facadeUvProjectionContext = TryCreateFacadeUvProjectionContext(
                cityObject.PackageName,
                cityObject.Surfaces.Select(static surface => surface.ToProjectionModel()),
                cityObjectOrigin.ToProjectionModel(),
                cityObjectCartesian);

            foreach (ResolvedSurfaceMaterial resolvedSurface in materialGroup
                         .OrderBy(static surface => CreateStableSurfaceSortKey(surface.Surface), StringComparer.Ordinal))
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
            string materialKey = CreateBindingMaterialKey(
                cityObject.ActualMeshCode,
                representativeSurface.Material,
                representativeSurface.DepthOffset,
                representativeSurface.Material.TextureScale,
                representativeSurface.Surface.BaseColor,
                representativeSurface.Material.TextureOffset);
            submeshes.Add(new MeshSubmesh(materialIndex, materialKey, indices));
            materials.Add(CreateMaterialBinding(cityObject.ActualMeshCode, representativeSurface, materialKey, materialIndex));
        }

        return new ImportedCityObject(
            ObjectKey: cityObject.SlotKey,
            DisplayName: cityObject.DisplayName,
            PackageName: cityObject.PackageName,
            ActualMeshCode: cityObject.ActualMeshCode,
            DetailEntry: cityObject.DetailEntry,
            FinestDetailGroup: cityObject.DetailEntry,
            Transform: new Transform3D(ToContractFloat3(slotPosition)),
            Mesh: new ImportedMesh(vertices.ToArray(), submeshes.ToArray()),
            Materials: materials,
            SourceFileRelativePath: cityObject.SourceFileRelativePath);
    }

    private static GeodeticPoint GetCityObjectOrigin(ParsedCityObject cityObject)
    {
        if (cityObject.GeodeticOriginOverride is not null)
        {
            return cityObject.GeodeticOriginOverride;
        }

        List<GeodeticPoint> allPoints = cityObject.Surfaces.SelectMany(static surface => surface.Vertices).ToList();
        double minLatitude = allPoints.Min(static point => point.Latitude);
        double maxLatitude = allPoints.Max(static point => point.Latitude);
        double minLongitude = allPoints.Min(static point => point.Longitude);
        double maxLongitude = allPoints.Max(static point => point.Longitude);
        double minAltitude = allPoints.Min(static point => point.Altitude);

        return new GeodeticPoint(
            Latitude: (minLatitude + maxLatitude) / 2.0,
            Longitude: (minLongitude + maxLongitude) / 2.0,
            Altitude: minAltitude);
    }

    private static global::PlateauResoniteLink.Application.Importing.GeodeticPoint GetCityObjectOrigin(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject)
    {
        if (cityObject.GeodeticOriginOverride is not null)
        {
            return cityObject.GeodeticOriginOverride;
        }

        List<global::PlateauResoniteLink.Application.Importing.GeodeticPoint> allPoints =
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices).ToList();
        double minLatitude = allPoints.Min(static point => point.Latitude);
        double maxLatitude = allPoints.Max(static point => point.Latitude);
        double minLongitude = allPoints.Min(static point => point.Longitude);
        double maxLongitude = allPoints.Max(static point => point.Longitude);
        double minAltitude = allPoints.Min(static point => point.Altitude);

        return new global::PlateauResoniteLink.Application.Importing.GeodeticPoint(
            Latitude: (minLatitude + maxLatitude) / 2.0,
            Longitude: (minLongitude + maxLongitude) / 2.0,
            Altitude: minAltitude);
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
                    ReuseScope: MaterialReuseScope.Shared,
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
        ResolvedMaterial resolvedMaterial = materialResolver.ResolveMaterial(
            cityObject.PackageName,
            surface.TexturePayload,
            preferUvProjection,
            preferUvProjection && IsBuildingPackage(cityObject.PackageName) ? BundledDefaultMaterialFamilies.Facade : null,
            $"{cityObject.SlotKey}:{(preferUvProjection ? "uv" : "triplanar")}");
        MaterialDepthOffset? depthOffset = cityObject.TerrainAligned
            ? DefaultTerrainAlignedMaterialDepthOffset
            : null;
        return new ResolvedSurfaceMaterial(surface, resolvedMaterial, depthOffset);
    }

    private static ResolvedSurfaceMaterial ResolveSurfaceMaterial(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        global::PlateauResoniteLink.Application.Importing.ParsedSurface surface,
        double cityObjectMinAltitude,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        ParsedSurface projectionSurface = surface.ToProjectionModel();
        if (projectionSurface.UsesGeneratedDemTexture)
        {
            return new ResolvedSurfaceMaterial(
                projectionSurface,
                new ResolvedMaterial(
                    MaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind.Dataset,
                    MaterialProjection.Uv,
                    Family: null,
                    TextureScale: null,
                    ReuseScope: MaterialReuseScope.Shared,
                    TerrainOverlay: demTerrainTextureOverlay),
                DepthOffset: null);
        }

        ResolvedMaterial? roofTerrainTextureMaterial = TryCreateRoofTerrainTextureMaterial(
            cityObject.ActualMeshCode,
            cityObject.PackageName,
            projectionSurface,
            cityObjectMinAltitude,
            demTerrainTextureOverlay,
            cityObjectOrigin.ToProjectionModel(),
            cityObjectCartesian);
        if (roofTerrainTextureMaterial is not null)
        {
            return new ResolvedSurfaceMaterial(
                projectionSurface with { BaseColor = DefaultMaterialColor },
                roofTerrainTextureMaterial,
                DepthOffset: null);
        }

        if (string.Equals(cityObject.PackageName, "veg", StringComparison.OrdinalIgnoreCase)
            && projectionSurface.TexturePayload is null)
        {
            if (HasExplicitMaterialColor(projectionSurface.BaseColor))
            {
                return new ResolvedSurfaceMaterial(
                    projectionSurface,
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
                projectionSurface with { BaseColor = DefaultVegetationMaterialColor },
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

        if (IsGeneratedRoadMarkingSurface(projectionSurface))
        {
            return new ResolvedSurfaceMaterial(
                projectionSurface,
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
            projectionSurface,
            cityObjectOrigin.ToProjectionModel(),
            cityObjectCartesian);
        ResolvedMaterial resolvedMaterial = materialResolver.ResolveMaterial(
            cityObject.PackageName,
            projectionSurface.TexturePayload,
            preferUvProjection,
            preferUvProjection && IsBuildingPackage(cityObject.PackageName) ? BundledDefaultMaterialFamilies.Facade : null,
            $"{cityObject.SlotKey}:{(preferUvProjection ? "uv" : "triplanar")}");
        MaterialDepthOffset? depthOffset = cityObject.TerrainAligned
            ? DefaultTerrainAlignedMaterialDepthOffset
            : null;
        return new ResolvedSurfaceMaterial(projectionSurface, resolvedMaterial, depthOffset);
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
            || ResolveTerrainTextureMeshCode(actualMeshCode, demTerrainTextureOverlay) is null
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
            ReuseScope: MaterialReuseScope.Shared,
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

        if (surface.Semantic != ParsedSurfaceSemantic.Unknown)
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

    private static global::PlateauResoniteLink.Application.Importing.ParsedCityObject? CreateGeneratedRoadMarkingCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (!string.Equals(cityObject.PackageName, "tran", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        List<global::PlateauResoniteLink.Application.Importing.ParsedSurface> markingSurfaces = [];
        foreach (global::PlateauResoniteLink.Application.Importing.ParsedSurface surface in cityObject.Surfaces)
        {
            if (surface.TexturePayload is not null)
            {
                continue;
            }

            List<global::PlateauResoniteLink.Application.Importing.ParsedSurface> generatedSurfaces =
                CreateGeneratedRoadMarkingSurfaces(surface, cityObjectOrigin, cityObjectCartesian);
            if (generatedSurfaces.Count == 0)
            {
                continue;
            }

            markingSurfaces.AddRange(generatedSurfaces);
        }

        return markingSurfaces.Count == 0
            ? null
            : cityObject with
            {
                SlotKey = $"{cityObject.SlotKey}_road_marking",
                DisplayName = $"{cityObject.DisplayName} Marking",
                Surfaces = markingSurfaces.ToArray(),
            };
    }

    private static List<ParsedSurface> CreateGeneratedRoadMarkingSurfaces(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        GeodeticPoint[] vertices = surface.ExteriorRing.Vertices;
        if (vertices.Length != 4 || surface.InteriorRings.Length != 0)
        {
            return [];
        }

        Float3[] positions = vertices
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        Float3? normal = ComputePolygonNormal(positions);
        if (normal is null || Math.Abs(normal.Y) < 0.7)
        {
            return [];
        }

        EdgePairSelection edgePair = SelectPrimaryRoadEdgePair(vertices, positions);
        if (edgePair.Length < 1.0 || edgePair.Width < 0.3)
        {
            return [];
        }

        double markingWidth = Math.Min(DefaultGeneratedRoadMarkingWidthMeters, edgePair.Width * 0.5);
        double insetDistance = Math.Max((edgePair.Width - markingWidth) * 0.5, 0.0);
        if (insetDistance <= 1e-6)
        {
            return [];
        }

        int segmentCount = Math.Max(
            1,
            (int)Math.Ceiling(edgePair.Length / DefaultGeneratedRoadMarkingSegmentLengthMeters));
        List<ParsedSurface> segments = new(segmentCount);

        for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            double startT = (double)segmentIndex / segmentCount;
            double endT = (double)(segmentIndex + 1) / segmentCount;
            GeodeticPoint side0Start = InterpolateAlongEdge(edgePair.Side0[0], edgePair.Side0[1], startT);
            GeodeticPoint side0End = InterpolateAlongEdge(edgePair.Side0[0], edgePair.Side0[1], endT);
            GeodeticPoint side1Start = InterpolateAlongEdge(edgePair.Side1[0], edgePair.Side1[1], startT);
            GeodeticPoint side1End = InterpolateAlongEdge(edgePair.Side1[0], edgePair.Side1[1], endT);

            GeodeticPoint[] side0Source = [side0Start, side0End];
            GeodeticPoint[] side1Source = [side1Start, side1End];
            Float3[] side0Positions = side0Source
                .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
                .ToArray();
            Float3[] side1Positions = side1Source
                .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
                .ToArray();

            GeodeticPoint[] side0 = MoveTowardCrossSection(
                side0Source,
                side1Source,
                side0Positions,
                side1Positions,
                insetDistance);
            GeodeticPoint[] side1 = MoveTowardCrossSection(
                side1Source,
                side0Source,
                side1Positions,
                side0Positions,
                insetDistance);
            segments.Add(new ParsedSurface(
                $"{surface.PolygonId}_generated_marking_{segmentIndex:D2}",
                surface.Semantic,
                new ParsedRing(
                    $"{surface.ExteriorRing.RingId}_generated_marking_{segmentIndex:D2}",
                    [side0[0], side0[1], side1[1], side1[0]],
                    UVs: null),
                [],
                DefaultRoadMarkingColor,
                TexturePayload: null));
        }

        return segments;
    }

    private static List<global::PlateauResoniteLink.Application.Importing.ParsedSurface> CreateGeneratedRoadMarkingSurfaces(
        global::PlateauResoniteLink.Application.Importing.ParsedSurface surface,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        return CreateGeneratedRoadMarkingSurfaces(
                surface.ToProjectionModel(),
                cityObjectOrigin.ToProjectionModel(),
                cityObjectCartesian)
            .Select(global::PlateauResoniteLink.Application.Importing.ParsedSurface.FromProjectionModel)
            .ToList();
    }

    private static EdgePairSelection SelectPrimaryRoadEdgePair(
        GeodeticPoint[] vertices,
        Float3[] positions)
    {
        double edge01 = Distance(positions[0], positions[1]);
        double edge12 = Distance(positions[1], positions[2]);
        double edge23 = Distance(positions[2], positions[3]);
        double edge30 = Distance(positions[3], positions[0]);

        double pair01Length = (edge01 + edge23) * 0.5;
        double pair12Length = (edge12 + edge30) * 0.5;

        return pair01Length >= pair12Length
            ? new EdgePairSelection(
                [vertices[0], vertices[1]],
                [vertices[3], vertices[2]],
                [positions[0], positions[1]],
                [positions[3], positions[2]],
                Side0Uvs: null,
                Side1Uvs: null,
                pair01Length,
                (Distance(positions[0], positions[3]) + Distance(positions[1], positions[2])) * 0.5,
                edge01,
                edge23)
            : new EdgePairSelection(
                [vertices[1], vertices[2]],
                [vertices[0], vertices[3]],
                [positions[1], positions[2]],
                [positions[0], positions[3]],
                Side0Uvs: null,
                Side1Uvs: null,
                pair12Length,
                (Distance(positions[1], positions[0]) + Distance(positions[2], positions[3])) * 0.5,
                edge12,
                edge30);
    }

    private static EdgePairSelection SelectPrimaryRoadEdgePair(
        ParsedRing ring,
        Float3[] positions)
    {
        EdgePairSelection pair = SelectPrimaryRoadEdgePair(ring.Vertices, positions);
        if (ring.UVs is null || ring.UVs.Count != ring.Vertices.Length || ring.Vertices.Length != 4)
        {
            return pair;
        }

        bool usesFirstEdge = AreSamePoint(pair.Side0[0], ring.Vertices[0])
            && AreSamePoint(pair.Side0[1], ring.Vertices[1]);

        return usesFirstEdge
            ? pair with
            {
                Side0Uvs = [ring.UVs[0], ring.UVs[1]],
                Side1Uvs = [ring.UVs[3], ring.UVs[2]],
            }
            : pair with
            {
                Side0Uvs = [ring.UVs[1], ring.UVs[2]],
                Side1Uvs = [ring.UVs[0], ring.UVs[3]],
            };
    }

    // Adapted from PLATEAU-SDK-for-Unity Runtime/RoadAdjust/RnmModelAdjuster.cs.
    // Each source point moves toward the nearest target point, matching upstream behavior.
    // Upstream MIT license text is stored in THIRD_PARTY_LICENSES/PLATEAU-SDK-for-Unity-LICENSE.txt.
    private static GeodeticPoint[] MoveTowardCrossSection(
        GeodeticPoint[] sourceWay,
        GeodeticPoint[] targetWay,
        Float3[] sourcePositions,
        Float3[] targetPositions,
        double distance)
    {
        if (sourceWay.Length != 2
            || targetWay.Length != 2
            || sourcePositions.Length != 2
            || targetPositions.Length != 2
            || distance <= 0.0)
        {
            return sourceWay.ToArray();
        }

        GeodeticPoint[] moved = new GeodeticPoint[2];
        for (int index = 0; index < 2; index++)
        {
            GeodeticPoint source = sourceWay[index];
            int nearestTargetIndex = 0;
            double nearestDistanceSquared = double.MaxValue;
            for (int targetIndex = 0; targetIndex < targetPositions.Length; targetIndex++)
            {
                double deltaX = sourcePositions[index].X - targetPositions[targetIndex].X;
                double deltaY = sourcePositions[index].Y - targetPositions[targetIndex].Y;
                double deltaZ = sourcePositions[index].Z - targetPositions[targetIndex].Z;
                double distanceSquared = (deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ);
                if (distanceSquared < nearestDistanceSquared)
                {
                    nearestDistanceSquared = distanceSquared;
                    nearestTargetIndex = targetIndex;
                }
            }

            GeodeticPoint target = targetWay[nearestTargetIndex];
            double actualDistance = Math.Sqrt(nearestDistanceSquared);
            if (actualDistance <= 1e-8)
            {
                moved[index] = source;
                continue;
            }

            double moveRatio = Math.Min(distance, actualDistance) / actualDistance;
            moved[index] = Lerp(source, target, moveRatio);
        }

        return moved;
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
            || ResolveTerrainTextureMeshCode(cityObject.ActualMeshCode, demTerrainTextureOverlay) is not { } terrainMeshCode
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
            || ResolveTerrainTextureMeshCode(actualMeshCode, demTerrainTextureOverlay) is not { } terrainMeshCode
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

    private static string CreateMaterialKey(
        MaterialType materialType,
        TerrainTextureOverlay? terrainOverlay,
        TexturePayload? texturePayload,
        TextureSourceKind textureSourceKind,
        MaterialProjection projection,
        MaterialDepthOffset? depthOffset,
        Float2? textureScale,
        string? family,
        ColorRgba color,
        Float2? textureOffset = null)
    {
        string terrainToken = terrainOverlay is null ? "none" : CreateTerrainOverlayToken(terrainOverlay);
        string textureToken = texturePayload?.Identity ?? "none";
        string familyToken = string.IsNullOrWhiteSpace(family) ? "none" : family.ToLowerInvariant();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"material-{MaterialTypeToken(materialType)}-{ProjectionToken(projection)}-terrain-{terrainToken}-texture-{textureToken}-source-{textureSourceKind.ToString().ToLowerInvariant()}-family-{familyToken}-depth-{FormatDepth(depthOffset)}-scale-{FormatFloat2(textureScale)}-offset-{FormatFloat2(textureOffset)}-color-{FormatColor(color)}");
    }

    private static string CreateBindingMaterialKey(
        string actualMeshCode,
        ResolvedMaterial material,
        MaterialDepthOffset? depthOffset,
        Float2? textureScale,
        ColorRgba color,
        Float2? textureOffset = null)
    {
        if (material.TerrainOverlay is not null)
        {
            if (ResolveTerrainTextureMeshCode(actualMeshCode, material.TerrainOverlay) is null)
            {
                throw new InvalidOperationException("Terrain overlay material requires a third-level mesh code that matches the overlay geographic bounds.");
            }

            Float2? normalizedTextureScale = IsIdentityTextureScale(textureScale) ? null : textureScale;
            Float2? normalizedTextureOffset = IsZeroTextureOffset(textureOffset) ? null : textureOffset;
            return CreateMaterialKey(
                material.MaterialType,
                terrainOverlay: null,
                material.TexturePayload,
                material.TextureSourceKind,
                material.Projection,
                depthOffset,
                normalizedTextureScale,
                family: null,
                new ColorRgba(1.0, 1.0, 1.0, 1.0),
                normalizedTextureOffset);
        }

        if (material.ReuseScope == MaterialReuseScope.Shared)
        {
            string family = material.Family ?? throw new InvalidOperationException("Common material must provide a family.");
            int variantIndex = material.BundledVariantIndex ?? 0;
            Float2? effectiveTextureOffset = IsZeroTextureOffset(textureOffset) ? null : textureOffset;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"common-{family}-{variantIndex}-{ProjectionToken(material.Projection)}-scale-{FormatFloat2(textureScale)}-offset-{FormatFloat2(effectiveTextureOffset)}");
        }

        return CreateMaterialKey(
            material.MaterialType,
            material.TerrainOverlay,
            material.TexturePayload,
            material.TextureSourceKind,
            material.Projection,
            depthOffset,
            textureScale,
            material.Family,
            color,
            textureOffset);
    }

    private static MaterialGroupingKey CreateMaterialGroupingKey(
        string actualMeshCode,
        ResolvedMaterial material,
        MaterialDepthOffset? depthOffset,
        Float2? textureScale,
        ColorRgba color,
        Float2? textureOffset = null)
    {
        if (material.TerrainOverlay is not null)
        {
            if (ResolveTerrainTextureMeshCode(actualMeshCode, material.TerrainOverlay) is null)
            {
                throw new InvalidOperationException("Terrain overlay material requires a third-level mesh code that matches the overlay geographic bounds.");
            }

            return new MaterialGroupingKey(
                material.MaterialType,
                TexturePayloadIdentity: null,
                material.TextureSourceKind,
                material.Projection,
                depthOffset,
                IsIdentityTextureScale(textureScale) ? null : textureScale,
                Family: null,
                BaseColor: null,
                IsZeroTextureOffset(textureOffset) ? null : textureOffset,
                MaterialReuseScope.PerObject,
                material.BundledVariantIndex,
                TerrainOverlay: null);
        }

        return new MaterialGroupingKey(
            material.MaterialType,
            material.TexturePayload?.Identity,
            material.TextureSourceKind,
            material.Projection,
            depthOffset,
            textureScale,
            material.Family,
            color,
            textureOffset,
            material.ReuseScope,
            material.BundledVariantIndex,
            material.TerrainOverlay);
    }

    private static string CreateTerrainOverlayToken(TerrainTextureOverlay terrainOverlay)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"terrain-overlay-{terrainOverlay.PackageName.ToLowerInvariant()}-{terrainOverlay.SourceDescriptorKey}-bounds-{FormatBounds(terrainOverlay.GeographicBounds)}");
    }

    private static string? ResolveTerrainTextureMeshCode(
        string actualMeshCode,
        TerrainTextureOverlay terrainOverlay)
    {
        if (actualMeshCode.Length == 8
            && TryCreateMeshCodeBounds(actualMeshCode, out MeshCodeBounds? actualMeshBounds)
            && BoundsApproximatelyEqual(actualMeshBounds!, terrainOverlay.GeographicBounds))
        {
            return actualMeshCode;
        }

        if (actualMeshCode.Length != 6
            || !actualMeshCode.All(static character => character is >= '0' and <= '9'))
        {
            return null;
        }

        for (int latitudeIndex = 0; latitudeIndex < 10; latitudeIndex++)
        {
            for (int longitudeIndex = 0; longitudeIndex < 10; longitudeIndex++)
            {
                string thirdMeshCode = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{actualMeshCode}{latitudeIndex}{longitudeIndex}");
                if (TryCreateMeshCodeBounds(thirdMeshCode, out MeshCodeBounds? thirdMeshBounds)
                    && BoundsApproximatelyEqual(thirdMeshBounds!, terrainOverlay.GeographicBounds))
                {
                    return thirdMeshCode;
                }
            }
        }

        return null;
    }

    private static bool BoundsApproximatelyEqual(
        MeshCodeBounds meshBounds,
        GeographicRectangle geographicBounds)
    {
        const double tolerance = 1e-8;
        return Math.Abs(meshBounds.SouthLatitude - geographicBounds.MinLatitude) <= tolerance
            && Math.Abs(meshBounds.NorthLatitude - geographicBounds.MaxLatitude) <= tolerance
            && Math.Abs(meshBounds.WestLongitude - geographicBounds.MinLongitude) <= tolerance
            && Math.Abs(meshBounds.EastLongitude - geographicBounds.MaxLongitude) <= tolerance;
    }

    private static string? TryResolveThirdMeshCodeFromBounds(GeographicRectangle geographicBounds)
    {
        int firstLatitudeIndex = (int)Math.Floor(geographicBounds.MinLatitude * 1.5);
        int firstLongitudeIndex = (int)Math.Floor(geographicBounds.MinLongitude - 100.0);
        double firstSouthLatitude = firstLatitudeIndex / 1.5;
        double firstWestLongitude = 100.0 + firstLongitudeIndex;
        double secondLatitudeSpan = (40.0 / 60.0) / 8.0;
        double secondLongitudeSpan = 1.0 / 8.0;
        int secondLatitudeIndex = (int)Math.Floor((geographicBounds.MinLatitude - firstSouthLatitude) / secondLatitudeSpan);
        int secondLongitudeIndex = (int)Math.Floor((geographicBounds.MinLongitude - firstWestLongitude) / secondLongitudeSpan);
        double secondSouthLatitude = firstSouthLatitude + (secondLatitudeIndex * secondLatitudeSpan);
        double secondWestLongitude = firstWestLongitude + (secondLongitudeIndex * secondLongitudeSpan);
        double thirdLatitudeSpan = secondLatitudeSpan / 10.0;
        double thirdLongitudeSpan = secondLongitudeSpan / 10.0;
        int thirdLatitudeIndex = (int)Math.Floor((geographicBounds.MinLatitude - secondSouthLatitude) / thirdLatitudeSpan);
        int thirdLongitudeIndex = (int)Math.Floor((geographicBounds.MinLongitude - secondWestLongitude) / thirdLongitudeSpan);

        string candidate = string.Create(
            CultureInfo.InvariantCulture,
            $"{firstLatitudeIndex:D2}{firstLongitudeIndex:D2}{secondLatitudeIndex}{secondLongitudeIndex}{thirdLatitudeIndex}{thirdLongitudeIndex}");
        return TryCreateMeshCodeBounds(candidate, out MeshCodeBounds? candidateBounds)
            && BoundsApproximatelyEqual(candidateBounds!, geographicBounds)
            ? candidate
            : null;
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
        string.Create(
            CultureInfo.InvariantCulture,
            $"{FormatRounded(bounds.MinLatitude)}-{FormatRounded(bounds.MaxLatitude)}-{FormatRounded(bounds.MinLongitude)}-{FormatRounded(bounds.MaxLongitude)}");

    private static string FormatRounded(double value)
    {
        double rounded = Math.Round(value, 6, MidpointRounding.AwayFromZero);
        return (rounded == 0.0 ? 0.0 : rounded).ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static bool IsZeroTextureOffset(Float2? textureOffset)
    {
        return textureOffset is null
            || (Math.Abs(textureOffset.X) < 1e-9
                && Math.Abs(textureOffset.Y) < 1e-9);
    }

    private static bool IsIdentityTextureScale(Float2? textureScale)
    {
        return textureScale is null
            || (Math.Abs(textureScale.X - 1.0) < 1e-9
                && Math.Abs(textureScale.Y - 1.0) < 1e-9);
    }

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

    private static ParsedSurfaceSemantic ParseSurfaceSemantic(XElement polygonElement)
    {
        for (XElement? ancestor = polygonElement.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            ParsedSurfaceSemantic semantic = ancestor.Name.LocalName switch
            {
                "WallSurface" or "InteriorWallSurface" => ParsedSurfaceSemantic.Wall,
                "RoofSurface" => ParsedSurfaceSemantic.Roof,
                "GroundSurface" => ParsedSurfaceSemantic.Ground,
                "ClosureSurface" => ParsedSurfaceSemantic.Closure,
                "OuterCeilingSurface" => ParsedSurfaceSemantic.OuterCeiling,
                "OuterFloorSurface" => ParsedSurfaceSemantic.OuterFloor,
                _ => ParsedSurfaceSemantic.Unknown,
            };

            if (semantic != ParsedSurfaceSemantic.Unknown)
            {
                return semantic;
            }
        }

        return ParsedSurfaceSemantic.Unknown;
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

        double minimumY = surfaceInfos.Min(static info => info.MinimumY!.Value);
        double maximumY = surfaceInfos.Max(static info => info.MaximumY!.Value);
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

    internal static double[] ParseDoubles(string value)
    {
        return value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => double.Parse(token, CultureInfo.InvariantCulture))
            .ToArray();
    }

    internal static List<Float2> ParseTextureCoordinates(string value)
    {
        double[] ordinates = ParseDoubles(value);
        List<Float2> coordinates = [];
        for (int index = 0; index + 1 < ordinates.Length; index += 2)
        {
            coordinates.Add(new Float2(ordinates[index], ordinates[index + 1]));
        }

        if (coordinates.Count > 1 && AreSameUV(coordinates[0], coordinates[^1]))
        {
            coordinates.RemoveAt(coordinates.Count - 1);
        }

        return coordinates;
    }

    private static bool AreSamePoint(GeodeticPoint left, GeodeticPoint right)
    {
        return Math.Abs(left.Latitude - right.Latitude) < 1e-8
            && Math.Abs(left.Longitude - right.Longitude) < 1e-8
            && Math.Abs(left.Altitude - right.Altitude) < 1e-8;
    }

    private static GeodeticPoint Lerp(GeodeticPoint source, GeodeticPoint target, double ratio)
    {
        return new GeodeticPoint(
            source.Latitude + ((target.Latitude - source.Latitude) * ratio),
            source.Longitude + ((target.Longitude - source.Longitude) * ratio),
            source.Altitude + ((target.Altitude - source.Altitude) * ratio));
    }

    private static GeodeticPoint InterpolateAlongEdge(GeodeticPoint start, GeodeticPoint end, double ratio)
    {
        return Lerp(start, end, ratio);
    }

    private static Float3 Lerp(Float3 source, Float3 target, double ratio)
    {
        return new Float3(
            source.X + ((target.X - source.X) * ratio),
            source.Y + ((target.Y - source.Y) * ratio),
            source.Z + ((target.Z - source.Z) * ratio));
    }

    private static Float2 Lerp(Float2 source, Float2 target, double ratio)
    {
        return new Float2(
            source.X + ((target.X - source.X) * ratio),
            source.Y + ((target.Y - source.Y) * ratio));
    }

    private static bool AreSameUV(Float2 left, Float2 right)
    {
        return Math.Abs(left.X - right.X) < 1e-8
            && Math.Abs(left.Y - right.Y) < 1e-8;
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
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
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
        Dictionary<string, DetailEntry> finestDetailGroupsBySlotKey = projectedInputCityObjects
            .GroupBy(static cityObject => cityObject.SlotKey, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static cityObject => cityObject.DetailEntry)
                    .OrderBy(static detailEntry => detailEntry.Order)
                    .ThenBy(static detailEntry => detailEntry.Key, StringComparer.Ordinal)
                    .FirstOrDefault(),
                StringComparer.Ordinal);

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
                         requestedMeshAreas,
                         terrainHeightSampler: null,
                         request,
                         materialResolver,
                         progressReporter,
                         cancellationToken))
            {
                yield return cityObject with
                {
                    FinestDetailGroup = finestDetailGroupsBySlotKey.TryGetValue(parsedCityObject.SlotKey, out DetailEntry finestDetailGroup)
                        ? finestDetailGroup
                        : parsedCityObject.DetailEntry,
                };
            }
        }
    }

    internal static IEnumerable<ImportedCityObject> ProjectParsedCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject parsedCityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
        TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsedCityObject);
        ArgumentNullException.ThrowIfNull(globalOriginPoint);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(materialResolver);

        global::PlateauResoniteLink.Application.Importing.ParsedCityObject terrainAlignedParsedCityObject =
            ConformCityObjectToTerrain(parsedCityObject, terrainHeightSampler);
        List<ImportedCityObject> projectedCityObjects = [];
        List<ImportedCityObject> generatedRoadMarkings = [];

        foreach ((global::PlateauResoniteLink.Application.Importing.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) splitCityObject
                 in SplitParsedCityObjectForTerrainProjection(
                     terrainAlignedParsedCityObject,
                     demTerrainTextureOverlays,
                     requestedMeshAreas,
                     progressReporter,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ShouldProjectTerrainOverlaySplit(splitCityObject.CityObject.ActualMeshCode, request.MeshCode, splitCityObject.Overlay))
            {
                throw new InvalidOperationException("Terrain overlay material requires a third-level mesh code that matches the overlay geographic bounds.");
            }

            ImportedCityObject cityObject = ProjectTerrainMeshModeCityObject(
                splitCityObject.CityObject,
                globalOriginPoint,
                globalCartesian,
                splitCityObject.Overlay,
                request,
                materialResolver,
                progressReporter,
                cancellationToken);

            if (HasRenderableGeometry(cityObject))
            {
                projectedCityObjects.Add(cityObject);
            }

            global::PlateauResoniteLink.Application.Importing.GeodeticPoint markingOrigin = GetCityObjectOrigin(splitCityObject.CityObject);
            LocalCartesian? markingCartesian = splitCityObject.CityObject.ReferenceSystem.IsGeographic
                ? new LocalCartesian(
                    markingOrigin.Latitude,
                    markingOrigin.Longitude,
                    markingOrigin.Altitude,
                    splitCityObject.CityObject.ReferenceSystem.Geocentric)
                : null;
            global::PlateauResoniteLink.Application.Importing.ParsedCityObject? roadMarkingCityObject = CreateGeneratedRoadMarkingCityObject(
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
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas,
        TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver)
    {
        ArgumentNullException.ThrowIfNull(parsedCityObject);
        ArgumentNullException.ThrowIfNull(globalOriginPoint);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(materialResolver);

        global::PlateauResoniteLink.Application.Importing.ParsedCityObject terrainAlignedParsedCityObject =
            ConformCityObjectToTerrain(parsedCityObject, terrainHeightSampler);

        foreach ((global::PlateauResoniteLink.Application.Importing.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) splitCityObject
                 in SplitParsedCityObjectForTerrainProjection(
                     terrainAlignedParsedCityObject,
                     demTerrainTextureOverlays,
                     requestedMeshAreas))
        {
            if (!ShouldProjectTerrainOverlaySplit(splitCityObject.CityObject.ActualMeshCode, request.MeshCode, splitCityObject.Overlay))
            {
                throw new InvalidOperationException("Terrain overlay material requires a third-level mesh code that matches the overlay geographic bounds.");
            }

            global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin = GetCityObjectOrigin(splitCityObject.CityObject);
            LocalCartesian? cityObjectCartesian = splitCityObject.CityObject.ReferenceSystem.IsGeographic
                ? new LocalCartesian(
                    cityObjectOrigin.Latitude,
                    cityObjectOrigin.Longitude,
                    cityObjectOrigin.Altitude,
                    splitCityObject.CityObject.ReferenceSystem.Geocentric)
                : null;

            foreach (MaterialBinding material in request.TerrainMeshMode is TerrainMeshMode.Grid or TerrainMeshMode.Dynamic
                         && string.Equals(splitCityObject.CityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
                            ? CreateDemTerrainGridMaterials(
                                splitCityObject.CityObject,
                                cityObjectOrigin,
                                cityObjectCartesian,
                                splitCityObject.Overlay,
                                request.MeshCode,
                                materialResolver)
                            : CreateCommonMaterialBindings(
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
            cityObject.DetailEntry,
            cityObject.DetailEntry,
            new Transform3D(new Float3(0.0, 0.0, 0.0)),
            new TriangleMeshGeometry(new ImportedMesh([], [])),
            [],
            SourceFileRelativePath: cityObject.SourceFileRelativePath);
    }

    private static global::PlateauResoniteLink.Application.Importing.ParsedCityObject ConformCityObjectToTerrain(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject parsedCityObject,
        TerrainHeightSampler? terrainHeightSampler)
    {
        if (terrainHeightSampler is null
            || !ShouldTerrainAlignCityObject(parsedCityObject))
        {
            return parsedCityObject;
        }

        global::PlateauResoniteLink.Application.Importing.ParsedCityObject subdividedCityObject =
            SubdivideTerrainAlignedCityObject(parsedCityObject);
        bool terrainAligned = false;
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin = GetCityObjectOrigin(subdividedCityObject);
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
                        subdividedCityObject.Surfaces.Select(static surface => surface.ToProjectionModel()).ToArray(),
                        terrainHeightSampler,
                        ref terrainAligned)
                    .Select(global::PlateauResoniteLink.Application.Importing.ParsedSurface.FromProjectionModel)
                    .ToArray()
                : ConformSurfacesToTerrain(
                        subdividedCityObject.PackageName,
                        subdividedCityObject.Surfaces.Select(static surface => surface.ToProjectionModel()).ToArray(),
                        terrainHeightSampler,
                        cityObjectOrigin.ToProjectionModel(),
                        cityObjectCartesian,
                        ref terrainAligned)
                    .Select(global::PlateauResoniteLink.Application.Importing.ParsedSurface.FromProjectionModel)
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
        return ShouldSubdivideTerrainAlignedCityObject(cityObject.PackageName, cityObject.DetailEntry);
    }

    private static bool ShouldTerrainAlignCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject)
    {
        return ShouldTerrainAlignCityObject(cityObject.PackageName, cityObject.DetailEntry);
    }

    private static bool ShouldSubdivideTerrainAlignedCityObject(string packageName, DetailEntry detailEntry)
    {
        return PlateauPackageCatalog.IsRoadPackage(packageName)
            && (detailEntry == DetailEntry.Default || detailEntry.Order > 1);
    }

    private static bool ShouldTerrainAlignCityObject(string packageName, DetailEntry detailEntry)
    {
        packageName = packageName.ToLowerInvariant();
        if (PlateauPackageCatalog.IsRoadPackage(packageName))
        {
            return detailEntry == DetailEntry.Default || detailEntry.Order > 1;
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
        double cityObjectMinAltitude = cityObject.Surfaces
            .SelectMany(static surface => surface.Vertices)
            .Min(static vertex => vertex.Altitude);
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
                resolvedSurface => CreateMaterialGroupingKey(
                    cityObject.ActualMeshCode,
                    resolvedSurface.Material,
                    resolvedSurface.DepthOffset,
                    resolvedSurface.Material.TextureScale,
                    resolvedSurface.Surface.BaseColor,
                    resolvedSurface.Material.TextureOffset))
            .OrderBy(static group => group.Min(static surface => CreateStableSurfaceSortKey(surface.Surface)), StringComparer.Ordinal)
            .Select((group, materialIndex) =>
            {
                ResolvedSurfaceMaterial representativeSurface = group.First();
                return CreateMaterialBinding(
                    cityObject.ActualMeshCode,
                    representativeSurface,
                    CreateBindingMaterialKey(
                        cityObject.ActualMeshCode,
                        representativeSurface.Material,
                        representativeSurface.DepthOffset,
                        representativeSurface.Material.TextureScale,
                        representativeSurface.Surface.BaseColor,
                        representativeSurface.Material.TextureOffset),
                    materialIndex);
            })
            .Where(static material => material.ReuseScope == MaterialReuseScope.Shared)
            .ToArray();
    }

    private static MaterialBinding[] CreateCommonMaterialBindings(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        ParsedSurface[] projectionSurfaces = cityObject.Surfaces.Select(static surface => surface.ToProjectionModel()).ToArray();
        HashSet<string> culledSurfaceIds = GetCulledSurfaceIdsBeforeProjection(
            cityObject.PackageName,
            projectionSurfaces,
            cityObjectOrigin.ToProjectionModel(),
            cityObjectCartesian);
        double cityObjectMinAltitude = projectionSurfaces
            .SelectMany(static surface => surface.Vertices)
            .Min(static vertex => vertex.Altitude);
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
                resolvedSurface => CreateMaterialGroupingKey(
                    cityObject.ActualMeshCode,
                    resolvedSurface.Material,
                    resolvedSurface.DepthOffset,
                    resolvedSurface.Material.TextureScale,
                    resolvedSurface.Surface.BaseColor,
                    resolvedSurface.Material.TextureOffset))
            .OrderBy(static group => group.Min(static surface => CreateStableSurfaceSortKey(surface.Surface)), StringComparer.Ordinal)
            .Select((group, materialIndex) =>
            {
                ResolvedSurfaceMaterial representativeSurface = group.First();
                return CreateMaterialBinding(
                    cityObject.ActualMeshCode,
                    representativeSurface,
                    CreateBindingMaterialKey(
                        cityObject.ActualMeshCode,
                        representativeSurface.Material,
                        representativeSurface.DepthOffset,
                        representativeSurface.Material.TextureScale,
                        representativeSurface.Surface.BaseColor,
                        representativeSurface.Material.TextureOffset),
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
        IDefaultMaterialResolver materialResolver,
        out ImportedCityObject? heightMapCityObject)
    {
        heightMapCityObject = null;

        GeodeticPoint cityObjectOrigin = GetCityObjectOrigin(cityObject);
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
            DetailEntry: cityObject.DetailEntry,
            FinestDetailGroup: cityObject.DetailEntry,
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

        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin = GetCityObjectOrigin(cityObject);
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

        MaterialBinding[] materials = CreateDemTerrainGridMaterials(
            cityObject,
            cityObjectOrigin,
            cityObjectCartesian,
            demTerrainTextureOverlay,
            request.MeshCode,
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
            DetailEntry: cityObject.DetailEntry,
            FinestDetailGroup: cityObject.DetailEntry,
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
        IDefaultMaterialResolver materialResolver)
    {
        HashSet<string> culledSurfaceIds = GetCulledSurfaceIdsBeforeProjection(
            cityObject.PackageName,
            cityObject.Surfaces,
            cityObjectOrigin,
            cityObjectCartesian);
        double cityObjectMinAltitude = cityObject.Surfaces
            .SelectMany(static surface => surface.Vertices)
            .Min(static vertex => vertex.Altitude);
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
                resolvedSurface => CreateMaterialGroupingKey(
                    ResolveTerrainTextureMaterialMeshCodeSource(cityObject.ActualMeshCode, requestedMeshCode, resolvedSurface.Material.TerrainOverlay),
                    resolvedSurface.Material,
                    resolvedSurface.DepthOffset,
                    resolvedSurface.Material.TextureScale,
                    resolvedSurface.Surface.BaseColor,
                    resolvedSurface.Material.TextureOffset))
            .OrderBy(static group => group.Min(static surface => CreateStableSurfaceSortKey(surface.Surface)), StringComparer.Ordinal)
            .Select((group, materialIndex) =>
            {
                ResolvedSurfaceMaterial representativeSurface = group.First();
                string terrainMaterialMeshCodeSource = ResolveTerrainTextureMaterialMeshCodeSource(
                    cityObject.ActualMeshCode,
                    requestedMeshCode,
                    representativeSurface.Material.TerrainOverlay);
                return CreateMaterialBinding(
                    terrainMaterialMeshCodeSource,
                    representativeSurface,
                    CreateBindingMaterialKey(
                        terrainMaterialMeshCodeSource,
                        representativeSurface.Material,
                        representativeSurface.DepthOffset,
                        representativeSurface.Material.TextureScale,
                        representativeSurface.Surface.BaseColor,
                        representativeSurface.Material.TextureOffset),
                    materialIndex);
            })
            .ToArray();
    }

    private static MaterialBinding[] CreateDemTerrainGridMaterials(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        string requestedMeshCode,
        IDefaultMaterialResolver materialResolver)
    {
        ParsedSurface[] projectionSurfaces = cityObject.Surfaces.Select(static surface => surface.ToProjectionModel()).ToArray();
        HashSet<string> culledSurfaceIds = GetCulledSurfaceIdsBeforeProjection(
            cityObject.PackageName,
            projectionSurfaces,
            cityObjectOrigin.ToProjectionModel(),
            cityObjectCartesian);
        double cityObjectMinAltitude = projectionSurfaces
            .SelectMany(static surface => surface.Vertices)
            .Min(static vertex => vertex.Altitude);
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
                resolvedSurface => CreateMaterialGroupingKey(
                    ResolveTerrainTextureMaterialMeshCodeSource(cityObject.ActualMeshCode, requestedMeshCode, resolvedSurface.Material.TerrainOverlay),
                    resolvedSurface.Material,
                    resolvedSurface.DepthOffset,
                    resolvedSurface.Material.TextureScale,
                    resolvedSurface.Surface.BaseColor,
                    resolvedSurface.Material.TextureOffset))
            .OrderBy(static group => group.Min(static surface => CreateStableSurfaceSortKey(surface.Surface)), StringComparer.Ordinal)
            .Select((group, materialIndex) =>
            {
                ResolvedSurfaceMaterial representativeSurface = group.First();
                string terrainMaterialMeshCodeSource = ResolveTerrainTextureMaterialMeshCodeSource(
                    cityObject.ActualMeshCode,
                    requestedMeshCode,
                    representativeSurface.Material.TerrainOverlay);
                return CreateMaterialBinding(
                    terrainMaterialMeshCodeSource,
                    representativeSurface,
                    CreateBindingMaterialKey(
                        terrainMaterialMeshCodeSource,
                        representativeSurface.Material,
                        representativeSurface.DepthOffset,
                        representativeSurface.Material.TextureScale,
                        representativeSurface.Surface.BaseColor,
                        representativeSurface.Material.TextureOffset),
                    materialIndex);
            })
            .ToArray();
    }

    private static MaterialBinding CreateMaterialBinding(
        string actualMeshCode,
        ResolvedSurfaceMaterial representativeSurface,
        string materialKey,
        int materialIndex)
    {
        string? terrainMeshCode = representativeSurface.Material.TerrainOverlay is null
            ? null
            : ResolveTerrainTextureMeshCode(actualMeshCode, representativeSurface.Material.TerrainOverlay)
                ?? throw new InvalidOperationException("Terrain overlay material requires a third-level mesh code that matches the overlay geographic bounds.");
        ColorRgba baseColor = representativeSurface.Material.TerrainOverlay is null
            ? ToContractColor(representativeSurface.Surface.BaseColor)
            : new ColorRgba(1.0, 1.0, 1.0, 1.0);
        return new MaterialBinding(
            MaterialKey: materialKey,
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
            TerrainMeshCode: terrainMeshCode);
    }

    private static string ResolveTerrainTextureMaterialMeshCodeSource(
        string actualMeshCode,
        string requestedMeshCode,
        TerrainTextureOverlay? terrainOverlay)
    {
        if (terrainOverlay is null
            || ResolveTerrainTextureMeshCode(actualMeshCode, terrainOverlay) is not null)
        {
            return actualMeshCode;
        }

        return requestedMeshCode;
    }

    private static IEnumerable<(global::PlateauResoniteLink.Application.Importing.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)> SplitParsedCityObjectForTerrainProjection(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        foreach ((global::PlateauResoniteLink.Application.Importing.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) splitCityObject
                 in DemTerrainOverlayAssignment.SplitParsedCityObject(
                     cityObject,
                     demTerrainTextureOverlays,
                     requestedMeshAreas,
                     progressReporter,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (splitCityObject.Overlay is not null
                || string.Equals(splitCityObject.CityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
            {
                yield return splitCityObject;
                continue;
            }

            foreach ((global::PlateauResoniteLink.Application.Importing.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) nonDemSplit
                     in SplitNonDemCityObjectByTerrainOverlay(
                         splitCityObject.CityObject,
                         demTerrainTextureOverlays,
                         progressReporter,
                         cancellationToken))
            {
                yield return nonDemSplit;
            }
        }
    }

    private static IEnumerable<(global::PlateauResoniteLink.Application.Importing.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)> SplitNonDemCityObjectByTerrainOverlay(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        if (demTerrainTextureOverlays.Count == 0 || !IsBuildingPackage(cityObject.PackageName))
        {
            yield return (cityObject, null);
            yield break;
        }

        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin = GetCityObjectOrigin(cityObject);
        LocalCartesian? cityObjectCartesian = cityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                cityObject.ReferenceSystem.Geocentric)
            : null;
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint[] cityObjectVertices =
        [
            .. cityObject.Surfaces.SelectMany(static surface => surface.Vertices)
        ];
        if (cityObjectVertices.Length == 0)
        {
            yield return (cityObject, null);
            yield break;
        }

        double cityObjectMinAltitude = cityObjectVertices.Min(static vertex => vertex.Altitude);

        List<global::PlateauResoniteLink.Application.Importing.ParsedSurface> untexturedSurfaces = [];
        List<(global::PlateauResoniteLink.Application.Importing.ParsedSurface Surface, TerrainTextureOverlay Overlay)> terrainOverlaySurfaces = [];
        foreach (global::PlateauResoniteLink.Application.Importing.ParsedSurface surface in cityObject.Surfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsNonDemTerrainTextureSurface(cityObject, surface, cityObjectMinAltitude, cityObjectOrigin, cityObjectCartesian)
                || !TryCreateSurfaceGeographicBounds(surface, out GeographicRectangle surfaceBounds))
            {
                untexturedSurfaces.Add(surface);
                continue;
            }

            TerrainTextureOverlay[] candidateOverlays = demTerrainTextureOverlays
                .Where(overlay => ResolveTerrainTextureMeshCode(cityObject.ActualMeshCode, overlay) is not null)
                .Where(overlay => BoundsOverlap(surfaceBounds, overlay.GeographicBounds))
                .OrderBy(static overlay => overlay.GeographicBounds.MinLatitude)
                .ThenBy(static overlay => overlay.GeographicBounds.MinLongitude)
                .ToArray();
            if (candidateOverlays.Length == 0)
            {
                if (demTerrainTextureOverlays.Any(overlay => BoundsOverlap(surfaceBounds, overlay.GeographicBounds)))
                {
                    throw new InvalidOperationException("Terrain overlay material requires a third-level mesh code that matches the overlay geographic bounds.");
                }

                untexturedSurfaces.Add(surface);
                continue;
            }

            if (candidateOverlays.Length == 1)
            {
                terrainOverlaySurfaces.Add((surface, candidateOverlays[0]));
                continue;
            }

            TerrainTextureOverlay? containingOverlay = candidateOverlays.FirstOrDefault(overlay =>
                ContainsBounds(overlay.GeographicBounds, surfaceBounds));
            if (containingOverlay is not null)
            {
                terrainOverlaySurfaces.Add((surface, containingOverlay));
                continue;
            }

            IReadOnlyList<(global::PlateauResoniteLink.Application.Importing.ParsedSurface Surface, TerrainTextureOverlay Overlay)> clippedSurfaces =
                DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToOverlays(
                    surface,
                    candidateOverlays,
                    progressReporter,
                    cancellationToken);
            if (clippedSurfaces.Count == 0)
            {
                untexturedSurfaces.Add(surface);
                continue;
            }

            terrainOverlaySurfaces.AddRange(clippedSurfaces);
        }

        IGrouping<TerrainTextureOverlay, (global::PlateauResoniteLink.Application.Importing.ParsedSurface Surface, TerrainTextureOverlay Overlay)>[] terrainGroups =
            terrainOverlaySurfaces
                .GroupBy(static entry => entry.Overlay)
                .OrderBy(static group => group.Key.GeographicBounds.MinLatitude)
                .ThenBy(static group => group.Key.GeographicBounds.MinLongitude)
                .ToArray();
        int splitCount = terrainGroups.Length + (untexturedSurfaces.Count == 0 ? 0 : 1);
        if (splitCount == 0)
        {
            yield break;
        }

        if (splitCount == 1)
        {
            if (terrainGroups.Length == 1)
            {
                yield return (
                    cityObject with
                    {
                        Surfaces = terrainGroups[0].Select(static entry => entry.Surface).ToArray(),
                        GeodeticOriginOverride = cityObjectOrigin,
                    },
                    terrainGroups[0].Key);
                yield break;
            }

            yield return (
                cityObject with
                {
                    Surfaces = untexturedSurfaces.ToArray(),
                    GeodeticOriginOverride = cityObjectOrigin,
                },
                null);
            yield break;
        }

        int splitIndex = 0;
        foreach (IGrouping<TerrainTextureOverlay, (global::PlateauResoniteLink.Application.Importing.ParsedSurface Surface, TerrainTextureOverlay Overlay)> group in terrainGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string terrainMeshCode = ResolveTerrainTextureMeshCode(cityObject.ActualMeshCode, group.Key)
                ?? splitIndex.ToString("D2", CultureInfo.InvariantCulture);
            yield return (
                cityObject with
                {
                    SlotKey = $"{cityObject.SlotKey}_terrain_{terrainMeshCode}",
                    DisplayName = $"{cityObject.DisplayName} ({splitIndex + 1})",
                    Surfaces = group.Select(static entry => entry.Surface).ToArray(),
                    GeodeticOriginOverride = cityObjectOrigin,
                },
                group.Key);
            splitIndex++;
        }

        if (untexturedSurfaces.Count != 0)
        {
            yield return (
                cityObject with
                {
                    SlotKey = $"{cityObject.SlotKey}_terrain_none",
                    DisplayName = $"{cityObject.DisplayName} ({splitIndex + 1})",
                    Surfaces = untexturedSurfaces.ToArray(),
                    GeodeticOriginOverride = cityObjectOrigin,
                },
                null);
        }
    }

    private static bool IsNonDemTerrainTextureSurface(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        global::PlateauResoniteLink.Application.Importing.ParsedSurface surface,
        double cityObjectMinAltitude,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        return surface.TexturePayload is null
            && !surface.UsesGeneratedDemTexture
            && IsRoofTerrainTextureSurface(
                surface.ToProjectionModel(),
                cityObjectMinAltitude,
                cityObjectOrigin.ToProjectionModel(),
                cityObjectCartesian);
    }

    private static bool ShouldProjectTerrainOverlaySplit(
        string actualMeshCode,
        string requestedMeshCode,
        TerrainTextureOverlay? terrainOverlay)
    {
        if (terrainOverlay is null)
        {
            return true;
        }

        if (requestedMeshCode.Length == 8
            && TryCreateMeshCodeBounds(requestedMeshCode, out MeshCodeBounds? requestedMeshBounds))
        {
            return BoundsApproximatelyEqual(requestedMeshBounds!, terrainOverlay.GeographicBounds);
        }

        return ResolveTerrainTextureMeshCode(actualMeshCode, terrainOverlay) is not null;
    }

    private static bool BoundsOverlap(MeshCodeBounds meshBounds, GeographicRectangle geographicBounds)
    {
        return meshBounds.NorthLatitude >= geographicBounds.MinLatitude
            && meshBounds.SouthLatitude <= geographicBounds.MaxLatitude
            && meshBounds.EastLongitude >= geographicBounds.MinLongitude
            && meshBounds.WestLongitude <= geographicBounds.MaxLongitude;
    }

    private static bool BoundsOverlap(GeographicRectangle left, GeographicRectangle right)
    {
        return left.MaxLatitude >= right.MinLatitude
            && left.MinLatitude <= right.MaxLatitude
            && left.MaxLongitude >= right.MinLongitude
            && left.MinLongitude <= right.MaxLongitude;
    }

    private static bool ContainsBounds(GeographicRectangle outer, GeographicRectangle inner)
    {
        return inner.MinLatitude >= outer.MinLatitude
            && inner.MaxLatitude <= outer.MaxLatitude
            && inner.MinLongitude >= outer.MinLongitude
            && inner.MaxLongitude <= outer.MaxLongitude;
    }

    private static bool TryCreateSurfaceGeographicBounds(
        global::PlateauResoniteLink.Application.Importing.ParsedSurface surface,
        out GeographicRectangle bounds)
    {
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint[] vertices = surface.ExteriorRing.Vertices;
        if (vertices.Length == 0)
        {
            bounds = new GeographicRectangle(0.0, 0.0, 0.0, 0.0);
            return false;
        }

        bounds = new GeographicRectangle(
            vertices.Min(static vertex => vertex.Latitude),
            vertices.Max(static vertex => vertex.Latitude),
            vertices.Min(static vertex => vertex.Longitude),
            vertices.Max(static vertex => vertex.Longitude));
        return true;
    }

    private static Float2 ToContractFloat2(Float2 value) => new(value.X, value.Y);

    private static Float2 ToContractFloat2(ScalarPair value) => new(value.X, value.Y);

    private static Float2 ToInternalFloat2(Float2 value) => new(value.X, value.Y);

    private static ColorRgba ToInternalColor(ColorRgba value) => new(value.R, value.G, value.B, value.A);

    private static MaterialOpticalProperties? CreateMaterialOpticalProperties(CityGmlMaterialAttributes? attributes)
    {
        if (attributes is null)
        {
            return null;
        }

        return new MaterialOpticalProperties(
            DiffuseColor: ToInternalColor(attributes.DiffuseColor),
            EmissiveColor: attributes.EmissiveColor is null ? null : ToInternalColor(attributes.EmissiveColor),
            SpecularColor: attributes.SpecularColor is null ? null : ToInternalColor(attributes.SpecularColor),
            AmbientIntensity: attributes.AmbientIntensity,
            Shininess: attributes.Shininess,
            Transparency: attributes.Transparency);
    }

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
        double cityObjectMinAltitude = cityObject.Surfaces
            .SelectMany(static surface => surface.Vertices)
            .Min(static vertex => vertex.Altitude);
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
            global::PlateauResoniteLink.Application.Importing.ParsedCityObject.FromProjectionModel(cityObject),
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
        double cityObjectMinAltitude = cityObject.Surfaces
            .SelectMany(static surface => surface.Vertices)
            .Min(static vertex => vertex.Altitude);
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

        return GetCityObjectGeographicBounds(cityObject);
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

        return GetCityObjectGeographicBounds(cityObject);
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
            GetCityObjectGeographicBounds(cityObject),
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
            GetCityObjectGeographicBounds(cityObject),
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

    private static GeographicRectangle GetCityObjectGeographicBounds(ParsedCityObject cityObject)
    {
        List<GeodeticPoint> vertices = cityObject.Surfaces.SelectMany(static surface => surface.Vertices).ToList();
        return new GeographicRectangle(
            MinLatitude: vertices.Min(static point => point.Latitude),
            MaxLatitude: vertices.Max(static point => point.Latitude),
            MinLongitude: vertices.Min(static point => point.Longitude),
            MaxLongitude: vertices.Max(static point => point.Longitude));
    }

    private static GeographicRectangle GetCityObjectGeographicBounds(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject)
    {
        List<global::PlateauResoniteLink.Application.Importing.GeodeticPoint> vertices =
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices).ToList();
        return new GeographicRectangle(
            MinLatitude: vertices.Min(static point => point.Latitude),
            MaxLatitude: vertices.Max(static point => point.Latitude),
            MinLongitude: vertices.Min(static point => point.Longitude),
            MaxLongitude: vertices.Max(static point => point.Longitude));
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
        DetailEntry DetailEntry,
        ParsedSurface[] Surfaces,
        CoordinateReferenceSystem ReferenceSystem,
        string SourceFileRelativePath,
        bool SharedAcrossMeshCodes,
        bool TerrainAligned = false,
        GeodeticPoint? GeodeticOriginOverride = null,
        int? FloorsAboveGround = null,
        double? MeasuredHeightMeters = null);

    internal sealed record SourceFileDescriptor(
        string RelativePath,
        string PackageName,
        string MatchedMeshCode,
        bool RequiresMeshAreaFilter);

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
        TerrainHeightTriangle[] TerrainTriangles,
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
        private readonly int[] allTriangleIndices;
        private readonly List<int>[] triangleBuckets;
        private readonly double minX;
        private readonly double minZ;
        private readonly double inverseCellSizeX;
        private readonly double inverseCellSizeZ;
        private readonly int cellsX;
        private readonly int cellsZ;

        private TerrainGridSpatialIndex(
            int[] allTriangleIndices,
            List<int>[] triangleBuckets,
            double minX,
            double minZ,
            double inverseCellSizeX,
            double inverseCellSizeZ,
            int cellsX,
            int cellsZ)
        {
            this.allTriangleIndices = allTriangleIndices;
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
            int[] allTriangleIndices = Enumerable.Range(0, triangles.Count).ToArray();
            if (triangles.Count == 0)
            {
                return new TerrainGridSpatialIndex(allTriangleIndices, [], minX, minZ, 1.0, 1.0, 1, 1);
            }

            double extentX = Math.Max(maxX - minX, 1e-6);
            double extentZ = Math.Max(maxZ - minZ, 1e-6);
            double aspectRatio = extentX / extentZ;
            double baseCellCount = Math.Ceiling(Math.Sqrt(triangles.Count));
            int cellsX = Math.Clamp((int)Math.Ceiling(baseCellCount * Math.Sqrt(aspectRatio)), 1, 256);
            int cellsZ = Math.Clamp((int)Math.Ceiling(baseCellCount / Math.Sqrt(aspectRatio)), 1, 256);
            double cellSizeX = extentX / cellsX;
            double cellSizeZ = extentZ / cellsZ;
            List<int>[] triangleBuckets = new List<int>[cellsX * cellsZ];

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
                        (triangleBuckets[bucketIndex] ??= []).Add(triangleIndex);
                    }
                }
            }

            return new TerrainGridSpatialIndex(
                allTriangleIndices,
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
            List<int>? bucket = triangleBuckets[(cellZ * cellsX) + cellX];
            return bucket is { Count: > 0 } ? bucket : allTriangleIndices;
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

    private sealed record EdgePairSelection(
        GeodeticPoint[] Side0,
        GeodeticPoint[] Side1,
        Float3[] Side0Positions,
        Float3[] Side1Positions,
        Float2[]? Side0Uvs,
        Float2[]? Side1Uvs,
        double Length,
        double Width,
        double Side0EdgeLength,
        double Side1EdgeLength);

    private readonly record struct SurfaceSliceSample(
        GeodeticPoint Point,
        Float2? UV,
        double LateralPosition);

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

    internal sealed record ResolvedSurfaceMaterial(
        ParsedSurface Surface,
        ResolvedMaterial Material,
        MaterialDepthOffset? DepthOffset);

    private sealed record MaterialGroupingKey(
        MaterialType MaterialType,
        string? TexturePayloadIdentity,
        TextureSourceKind TextureSourceKind,
        MaterialProjection Projection,
        MaterialDepthOffset? DepthOffset,
        Float2? TextureScale,
        string? Family,
        ColorRgba? BaseColor,
        Float2? TextureOffset,
        MaterialReuseScope ReuseScope,
        int? BundledVariantIndex,
        TerrainTextureOverlay? TerrainOverlay);

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

    private sealed record TerrainHeightPoint(
        double Latitude,
        double Longitude,
        double Altitude,
        double X,
        double Z);

    internal sealed record TerrainHeightTriangle(
        GeodeticPoint Vertex0,
        GeodeticPoint Vertex1,
        GeodeticPoint Vertex2);

    private sealed record ProjectedTerrainHeightTriangle(
        TerrainHeightPoint Vertex0,
        TerrainHeightPoint Vertex1,
        TerrainHeightPoint Vertex2,
        double MinX,
        double MaxX,
        double MinZ,
        double MaxZ);

    internal sealed class TerrainHeightSampler
    {
        private readonly LocalCartesian cartesian;
        private readonly double cellSize;
        private readonly double maxX;
        private readonly double maxZ;
        private readonly double minX;
        private readonly double minZ;
        private readonly int maxCellSearchRadius;
        private readonly TerrainHeightPoint[] points;
        private readonly Dictionary<TerrainGridCell, TerrainHeightPoint[]> pointsByCell;
        private readonly ProjectedTerrainHeightTriangle[] triangles;
        private readonly Dictionary<TerrainGridCell, ProjectedTerrainHeightTriangle[]> trianglesByCell;

        private TerrainHeightSampler(
            LocalCartesian cartesian,
            double minX,
            double maxX,
            double minZ,
            double maxZ,
            double cellSize,
            TerrainHeightPoint[] points,
            ProjectedTerrainHeightTriangle[] triangles,
            Dictionary<TerrainGridCell, TerrainHeightPoint[]> pointsByCell,
            Dictionary<TerrainGridCell, ProjectedTerrainHeightTriangle[]> trianglesByCell)
        {
            this.cartesian = cartesian;
            this.cellSize = cellSize;
            this.maxX = maxX;
            this.maxZ = maxZ;
            this.minX = minX;
            this.minZ = minZ;
            maxCellSearchRadius = Math.Max(
                1,
                (int)Math.Ceiling(
                    Math.Max(maxX - minX, maxZ - minZ)
                    / Math.Max(cellSize, 1e-6)));
            this.points = points;
            this.pointsByCell = pointsByCell;
            this.triangles = triangles;
            this.trianglesByCell = trianglesByCell;
        }

        public static TerrainHeightSampler Create(
            IEnumerable<TerrainHeightTriangle> sourceTriangles,
            GeodeticPoint origin,
            Geocentric geocentric)
        {
            ArgumentNullException.ThrowIfNull(sourceTriangles);
            ArgumentNullException.ThrowIfNull(origin);
            ArgumentNullException.ThrowIfNull(geocentric);

            LocalCartesian cartesian = new(
                origin.Latitude,
                origin.Longitude,
                origin.Altitude,
                geocentric);
            List<ProjectedTerrainHeightTriangle> triangles = [];
            List<TerrainHeightPoint> points = [];

            foreach (TerrainHeightTriangle triangle in sourceTriangles)
            {
                TerrainHeightPoint point0 = CreatePoint(triangle.Vertex0, cartesian);
                TerrainHeightPoint point1 = CreatePoint(triangle.Vertex1, cartesian);
                TerrainHeightPoint point2 = CreatePoint(triangle.Vertex2, cartesian);
                triangles.Add(new ProjectedTerrainHeightTriangle(
                    point0,
                    point1,
                    point2,
                    Math.Min(point0.X, Math.Min(point1.X, point2.X)),
                    Math.Max(point0.X, Math.Max(point1.X, point2.X)),
                    Math.Min(point0.Z, Math.Min(point1.Z, point2.Z)),
                    Math.Max(point0.Z, Math.Max(point1.Z, point2.Z))));
                points.Add(point0);
                points.Add(point1);
                points.Add(point2);
            }

            if (points.Count == 0)
            {
                return new TerrainHeightSampler(
                    cartesian,
                    0.0,
                    0.0,
                    0.0,
                    0.0,
                    1.0,
                    [],
                    [],
                    new Dictionary<TerrainGridCell, TerrainHeightPoint[]>(),
                    new Dictionary<TerrainGridCell, ProjectedTerrainHeightTriangle[]>());
            }

            double minX = points.Min(static point => point.X);
            double maxX = points.Max(static point => point.X);
            double minZ = points.Min(static point => point.Z);
            double maxZ = points.Max(static point => point.Z);
            double cellSize = ComputeCellSize(minX, maxX, minZ, maxZ, triangles.Count);

            Dictionary<TerrainGridCell, TerrainHeightPoint[]> pointsByCell = CreatePointIndex(points, minX, minZ, cellSize);
            Dictionary<TerrainGridCell, ProjectedTerrainHeightTriangle[]> trianglesByCell =
                CreateTriangleIndex(triangles, minX, minZ, cellSize);

            return new TerrainHeightSampler(
                cartesian,
                minX,
                maxX,
                minZ,
                maxZ,
                cellSize,
                points.ToArray(),
                triangles.ToArray(),
                pointsByCell,
                trianglesByCell);
        }

        public bool TrySampleHeight(double latitude, double longitude, out double altitude, bool allowNearestPointFallback = true)
        {
            (double x, double z) = Project(latitude, longitude);
            if (x < minX - 1e-6
                || x > maxX + 1e-6
                || z < minZ - 1e-6
                || z > maxZ + 1e-6)
            {
                altitude = 0.0;
                return false;
            }

            TerrainGridCell cell = GetCell(x, z);
            foreach (ProjectedTerrainHeightTriangle triangle in GetCandidateTriangles(cell))
            {
                if (x < triangle.MinX - 1e-6
                    || x > triangle.MaxX + 1e-6
                    || z < triangle.MinZ - 1e-6
                    || z > triangle.MaxZ + 1e-6)
                {
                    continue;
                }

                if (TryInterpolateTriangleHeight(triangle, x, z, out altitude))
                {
                    return true;
                }
            }

            foreach (ProjectedTerrainHeightTriangle triangle in GetCandidateTriangles(cell, radius: 1))
            {
                if (x < triangle.MinX - 1e-6
                    || x > triangle.MaxX + 1e-6
                    || z < triangle.MinZ - 1e-6
                    || z > triangle.MaxZ + 1e-6)
                {
                    continue;
                }

                if (TryInterpolateTriangleHeight(triangle, x, z, out altitude))
                {
                    return true;
                }
            }

            if (allowNearestPointFallback)
            {
                return TrySampleNearestPointHeight(x, z, out altitude);
            }

            altitude = 0.0;
            return false;
        }

        private static TerrainHeightPoint CreatePoint(GeodeticPoint point, LocalCartesian cartesian)
        {
            (double x, _, double z) = cartesian.Forward(point.Latitude, point.Longitude, 0.0);
            return new TerrainHeightPoint(
                point.Latitude,
                point.Longitude,
                point.Altitude,
                x,
                z);
        }

        private (double X, double Z) Project(double latitude, double longitude)
        {
            (double x, _, double z) = cartesian.Forward(latitude, longitude, 0.0);
            return (x, z);
        }

        private static bool TryInterpolateTriangleHeight(
            ProjectedTerrainHeightTriangle triangle,
            double x,
            double z,
            out double altitude)
        {
            double ax = triangle.Vertex0.X;
            double az = triangle.Vertex0.Z;
            double bx = triangle.Vertex1.X;
            double bz = triangle.Vertex1.Z;
            double cx = triangle.Vertex2.X;
            double cz = triangle.Vertex2.Z;

            double denominator = ((bz - cz) * (ax - cx)) + ((cx - bx) * (az - cz));
            if (Math.Abs(denominator) < 1e-8)
            {
                altitude = 0.0;
                return false;
            }

            double weight0 = (((bz - cz) * (x - cx)) + ((cx - bx) * (z - cz))) / denominator;
            double weight1 = (((cz - az) * (x - cx)) + ((ax - cx) * (z - cz))) / denominator;
            double weight2 = 1.0 - weight0 - weight1;
            if (weight0 < -1e-5 || weight1 < -1e-5 || weight2 < -1e-5)
            {
                altitude = 0.0;
                return false;
            }

            altitude = (triangle.Vertex0.Altitude * weight0)
                + (triangle.Vertex1.Altitude * weight1)
                + (triangle.Vertex2.Altitude * weight2);
            return true;
        }

        private bool TrySampleNearestPointHeight(double x, double z, out double altitude)
        {
            if (points.Length == 0)
            {
                altitude = 0.0;
                return false;
            }

            TerrainGridCell cell = GetCell(x, z);
            List<TerrainHeightPoint> candidatePoints = [];
            for (int radius = 0; radius <= maxCellSearchRadius; radius++)
            {
                AppendCandidatePoints(candidatePoints, cell, radius);
                if (candidatePoints.Count >= 4)
                {
                    break;
                }
            }

            if (candidatePoints.Count == 0)
            {
                altitude = 0.0;
                return false;
            }

            ReadOnlySpan<TerrainHeightPoint> nearestPoints = SelectNearestPoints(candidatePoints, x, z);
            if (nearestPoints.Length == 0)
            {
                altitude = 0.0;
                return false;
            }

            double weightedAltitude = 0.0;
            double weightSum = 0.0;
            foreach (TerrainHeightPoint point in nearestPoints)
            {
                double distanceSquared = SquaredDistance(point, x, z);
                if (distanceSquared < 1e-8)
                {
                    altitude = point.Altitude;
                    return true;
                }

                double weight = 1.0 / distanceSquared;
                weightedAltitude += point.Altitude * weight;
                weightSum += weight;
            }

            if (weightSum < 1e-8)
            {
                altitude = 0.0;
                return false;
            }

            altitude = weightedAltitude / weightSum;
            return true;
        }

        private static double ComputeCellSize(
            double minX,
            double maxX,
            double minZ,
            double maxZ,
            int triangleCount)
        {
            if (triangleCount <= 0)
            {
                return 1.0;
            }

            double width = Math.Max(maxX - minX, 1.0);
            double depth = Math.Max(maxZ - minZ, 1.0);
            double area = width * depth;
            double estimatedCellArea = area / triangleCount;
            return Math.Max(1.0, Math.Sqrt(Math.Max(estimatedCellArea, 1e-6)));
        }

        private static Dictionary<TerrainGridCell, TerrainHeightPoint[]> CreatePointIndex(
            IEnumerable<TerrainHeightPoint> points,
            double minX,
            double minZ,
            double cellSize)
        {
            Dictionary<TerrainGridCell, List<TerrainHeightPoint>> buckets = [];

            foreach (TerrainHeightPoint point in points)
            {
                TerrainGridCell cell = GetCell(point.X, point.Z, minX, minZ, cellSize);
                if (!buckets.TryGetValue(cell, out List<TerrainHeightPoint>? bucket))
                {
                    bucket = [];
                    buckets[cell] = bucket;
                }

                bucket.Add(point);
            }

            return buckets.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.ToArray());
        }

        private static Dictionary<TerrainGridCell, ProjectedTerrainHeightTriangle[]> CreateTriangleIndex(
            IEnumerable<ProjectedTerrainHeightTriangle> triangles,
            double minX,
            double minZ,
            double cellSize)
        {
            Dictionary<TerrainGridCell, List<ProjectedTerrainHeightTriangle>> buckets = [];

            foreach (ProjectedTerrainHeightTriangle triangle in triangles)
            {
                TerrainGridCell minCell = GetCell(triangle.MinX, triangle.MinZ, minX, minZ, cellSize);
                TerrainGridCell maxCell = GetCell(triangle.MaxX, triangle.MaxZ, minX, minZ, cellSize);

                for (int x = minCell.X; x <= maxCell.X; x++)
                {
                    for (int z = minCell.Z; z <= maxCell.Z; z++)
                    {
                        TerrainGridCell cell = new(x, z);
                        if (!buckets.TryGetValue(cell, out List<ProjectedTerrainHeightTriangle>? bucket))
                        {
                            bucket = [];
                            buckets[cell] = bucket;
                        }

                        bucket.Add(triangle);
                    }
                }
            }

            return buckets.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.ToArray());
        }

        private IEnumerable<ProjectedTerrainHeightTriangle> GetCandidateTriangles(TerrainGridCell centerCell, int radius = 0)
        {
            if (radius == 0)
            {
                if (trianglesByCell.TryGetValue(centerCell, out ProjectedTerrainHeightTriangle[]? localTriangles))
                {
                    foreach (ProjectedTerrainHeightTriangle triangle in localTriangles)
                    {
                        yield return triangle;
                    }
                }

                yield break;
            }

            HashSet<ProjectedTerrainHeightTriangle> seen = [];
            foreach (TerrainGridCell cell in EnumerateCells(centerCell, radius))
            {
                if (!trianglesByCell.TryGetValue(cell, out ProjectedTerrainHeightTriangle[]? localTriangles))
                {
                    continue;
                }

                foreach (ProjectedTerrainHeightTriangle triangle in localTriangles)
                {
                    if (seen.Add(triangle))
                    {
                        yield return triangle;
                    }
                }
            }
        }

        private void AppendCandidatePoints(List<TerrainHeightPoint> destination, TerrainGridCell centerCell, int radius)
        {
            foreach (TerrainGridCell cell in EnumerateCells(centerCell, radius))
            {
                if (!pointsByCell.TryGetValue(cell, out TerrainHeightPoint[]? localPoints))
                {
                    continue;
                }

                destination.AddRange(localPoints);
            }
        }

        private static IEnumerable<TerrainGridCell> EnumerateCells(TerrainGridCell centerCell, int radius)
        {
            if (radius == 0)
            {
                yield return centerCell;
                yield break;
            }

            for (int x = centerCell.X - radius; x <= centerCell.X + radius; x++)
            {
                for (int z = centerCell.Z - radius; z <= centerCell.Z + radius; z++)
                {
                    if (Math.Abs(x - centerCell.X) != radius
                        && Math.Abs(z - centerCell.Z) != radius)
                    {
                        continue;
                    }

                    yield return new TerrainGridCell(x, z);
                }
            }
        }

        private static TerrainHeightPoint[] SelectNearestPoints(List<TerrainHeightPoint> candidates, double x, double z)
        {
            const int MaxNearestPoints = 4;
            TerrainHeightPoint[] nearestPoints = new TerrainHeightPoint[Math.Min(MaxNearestPoints, candidates.Count)];
            double[] nearestDistances = Enumerable.Repeat(double.PositiveInfinity, nearestPoints.Length).ToArray();
            int count = 0;

            foreach (TerrainHeightPoint candidate in candidates)
            {
                double distanceSquared = SquaredDistance(candidate, x, z);
                int insertIndex = count < nearestPoints.Length
                    ? count
                    : GetWorstIndex(nearestDistances);

                if (count == nearestPoints.Length && distanceSquared >= nearestDistances[insertIndex])
                {
                    continue;
                }

                nearestPoints[insertIndex] = candidate;
                nearestDistances[insertIndex] = distanceSquared;
                if (count < nearestPoints.Length)
                {
                    count++;
                }
            }

            if (count == nearestPoints.Length)
            {
                return nearestPoints;
            }

            TerrainHeightPoint[] trimmed = new TerrainHeightPoint[count];
            Array.Copy(nearestPoints, trimmed, count);
            return trimmed;
        }

        private static int GetWorstIndex(double[] distances)
        {
            int worstIndex = 0;
            double worstDistance = distances[0];

            for (int index = 1; index < distances.Length; index++)
            {
                if (distances[index] > worstDistance)
                {
                    worstDistance = distances[index];
                    worstIndex = index;
                }
            }

            return worstIndex;
        }

        private TerrainGridCell GetCell(double x, double z)
        {
            return GetCell(x, z, minX, minZ, cellSize);
        }

        private static TerrainGridCell GetCell(double x, double z, double minX, double minZ, double cellSize)
        {
            int cellX = (int)Math.Floor((x - minX) / cellSize);
            int cellZ = (int)Math.Floor((z - minZ) / cellSize);
            return new TerrainGridCell(cellX, cellZ);
        }

        private static double SquaredDistance(TerrainHeightPoint point, double x, double z)
        {
            double dx = point.X - x;
            double dz = point.Z - z;
            return (dx * dx) + (dz * dz);
        }
    }

    private readonly record struct TerrainGridCell(int X, int Z);

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
