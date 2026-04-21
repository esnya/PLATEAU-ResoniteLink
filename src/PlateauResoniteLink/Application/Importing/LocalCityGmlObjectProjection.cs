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

using PlateauResoniteLink.Domain.Importing;

using Geocentric = GeographicLib.Geocentric;
using LocalCartesian = GeographicLib.LocalCartesian;

namespace PlateauResoniteLink.Application.Importing;

public static partial class LocalCityGmlObjectProjection
{
    public const string DefaultDemTerrainTexturePath = "dem/plateau-ortho";
    public const string DefaultDemTerrainTextureUrlTemplate = "https://api.plateauview.mlit.go.jp/tiles/plateau-ortho-2023/{z}/{x}/{y}.png";
    public const string DefaultDemTerrainTextureFallbackUrlTemplate = "https://cyberjapandata.gsi.go.jp/xyz/seamlessphoto/{z}/{x}/{y}.jpg";
    public const int DefaultDemTerrainTextureZoomLevel = 19;
    public const int DefaultDemTerrainTextureFallbackZoomLevel = 18;
    public const int DefaultDemTerrainTextureMaxSize = 8192;
    public const double DefaultGeneratedRoadMarkingWidthMeters = 0.15;
    public const double DefaultGeneratedRoadMarkingSegmentLengthMeters = 5.0;
    public const double DefaultTerrainAlignedTransportationSegmentLengthMeters = 5.0;
    public const double MinTerrainAlignedTransportationSegmentLengthMeters = 2.0;
    public const double TerrainAlignedTransportationSegmentLengthByWidthRatio = 0.8;
    public static readonly ResoniteMaterialDepthOffset DefaultTerrainAlignedMaterialDepthOffset = new(-10.0, -10.0);

    private static readonly ResoniteFloatQ GridMeshTerrainRotation = new(
        X: Math.Sqrt(0.5),
        Y: 0.0,
        Z: 0.0,
        W: Math.Sqrt(0.5));
    private static readonly ResoniteColor DefaultMaterialColor = new(1.0, 1.0, 1.0, 1.0);
    private static readonly ResoniteColor DefaultVegetationMaterialColor = new(0.32, 0.58, 0.24, 1.0);
    private static readonly ResoniteColor DefaultRoadMarkingColor = new(1.0, 1.0, 1.0, 1.0);
    private static readonly XNamespace App = "http://www.opengis.net/citygml/appearance/2.0";
    private static readonly XNamespace Core = "http://www.opengis.net/citygml/2.0";
    private static readonly XNamespace Gml = "http://www.opengis.net/gml";
    private static readonly Regex ConcreteMeshCodeTokenRegex = new(
        @"(?<!\d)(\d{8})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);



    private static ParsedCityObject? ParseCityObject(
        XElement cityObjectElement,
        string packageName,
        string relativeSourceFile,
        string actualMeshCode,
        bool sharedAcrossMeshCodes,
        AppearanceLibrary appearanceLibrary,
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

        // Detect if this is a Marking object
        bool isMarking = displayName.Contains("Marking", StringComparison.OrdinalIgnoreCase)
            || objectId.Contains("Marking", StringComparison.OrdinalIgnoreCase)
            || objectId.Contains("_road_marking", StringComparison.Ordinal);

        (XElement[] preferredSurfaceElements, int? lodLevel) = SelectPreferredLodSurfaceElements(
            cityObjectElement,
            packageName,
            isMarking,
            lodFilteringStrategy);

        // Check if object should be excluded by LOD and pattern filters
        if (!lodFilteringStrategy.ShouldIncludeByPattern(packageName, objectId, isMarking))
        {
            return null;
        }

        if (preferredSurfaceElements.Length == 0 && lodFilteringStrategy.ShouldExcludeLod(packageName, lodLevel, isMarking))
        {
            return null;
        }

        ParsedSurface[] surfaces = preferredSurfaceElements
            .Select(surfaceElement => ParseSurface(surfaceElement, appearanceLibrary))
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

        string fileStem = Path.GetFileNameWithoutExtension(relativeSourceFile);
        string slotKey = SanitizeIdentifier($"{packageName}_{fileStem}_{objectId}");
        string sourceUnitIdentity = SanitizeIdentifier(relativeSourceFile);
        string sourceIdentity = SanitizeIdentifier($"{relativeSourceFile}_{objectId}");
        return new ParsedCityObject(
            slotKey,
            displayName!,
            packageName,
            resolvedActualMeshCode,
            lodLevel,
            surfaces,
            coordinateReferenceSystem,
            relativeSourceFile,
            sourceUnitIdentity,
            sourceIdentity,
            SharedAcrossMeshCodes: sharedAcrossMeshCodes);
    }

    private static ParsedCityObject? ParseCityObject(
        XElement cityObjectElement,
        string packageName,
        string relativeSourceFile,
        string actualMeshCode,
        bool sharedAcrossMeshCodes,
        ICityGmlAppearanceStore appearanceStore,
        ICityGmlLodSelector lodSelector,
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

        string fileStem = Path.GetFileNameWithoutExtension(relativeSourceFile);
        string slotKey = SanitizeIdentifier($"{packageName}_{fileStem}_{objectId}");
        string sourceUnitIdentity = SanitizeIdentifier(relativeSourceFile);
        string sourceIdentity = SanitizeIdentifier($"{relativeSourceFile}_{objectId}");
        return new ParsedCityObject(
            slotKey,
            displayName!,
            packageName,
            resolvedActualMeshCode,
            lodLevel,
            surfaces,
            coordinateReferenceSystem,
            relativeSourceFile,
            sourceUnitIdentity,
            sourceIdentity,
            SharedAcrossMeshCodes: sharedAcrossMeshCodes,
            FloorsAboveGround: floorsAboveGround,
            MeasuredHeightMeters: measuredHeightMeters);
    }

    internal static TerrainTextureOverlay[] CreateDemTerrainTextureOverlays(
        MeshCodeBounds demBounds,
        IReadOnlyList<string> requestedMeshCodes)
    {
        return LocalCityGmlDemBootstrapSupport.CreateDemTerrainOverlayRegions(
                DemTerrainBounds.FromLegacy(demBounds),
                requestedMeshCodes)
            .Select(static region => new TerrainTextureOverlay(
                PackageName: "dem",
                GeographicBounds: region.GeographicBounds,
                MaxTextureSize: DefaultDemTerrainTextureMaxSize,
                Sources:
                [
                    new TerrainTextureTileSource(DefaultDemTerrainTextureUrlTemplate, DefaultDemTerrainTextureZoomLevel),
                    new TerrainTextureTileSource(DefaultDemTerrainTextureUrlTemplate, DefaultDemTerrainTextureFallbackZoomLevel),
                    new TerrainTextureTileSource(DefaultDemTerrainTextureFallbackUrlTemplate, DefaultDemTerrainTextureFallbackZoomLevel),
                ],
                LicenseMode: TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback))
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

    private static ParsedSurface ApplyPackageSurfaceDefaults(string packageName, ParsedSurface surface)
    {
        return string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase)
            && surface.TexturePayload is null
            ? surface with { UsesGeneratedDemTexture = true }
            : surface;
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

    private static ParsedCityObject[] ParseCityObjects(
        XDocument document,
        SourceFileDescriptor sourceFile,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas,
        LodFilteringStrategy lodFilteringStrategy)
    {
        string relativeSourceFile = sourceFile.RelativePath;
        CoordinateReferenceSystem coordinateReferenceSystem = CoordinateReferenceSystem.Parse(document);
        AppearanceLibrary appearanceLibrary = AppearanceLibrary.Parse(
            document,
            relativeSourceFile,
            datasetSource);

        return document
            .Descendants(Core + "cityObjectMember")
            .Elements()
            .Select(cityObject => ParseCityObject(
                cityObject,
                sourceFile.PackageName,
                relativeSourceFile,
                sourceFile.MatchedMeshCode,
                sourceFile.RequiresMeshAreaFilter,
                appearanceLibrary,
                coordinateReferenceSystem,
                sourceFile.RequiresMeshAreaFilter ? requestedMeshAreas : null,
                lodFilteringStrategy))
            .Where(static cityObject => cityObject is not null)
            .Select(static cityObject => cityObject!)
            .OrderBy(static cityObject => cityObject.SlotKey, StringComparer.Ordinal)
            .ToArray();
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

        return int.TryParse(element.Value.Trim(), CultureInfo.InvariantCulture, out int value) && value >= 0
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

            IReadOnlyList<ResoniteFloat2>? uvs = ring.UVs;
            writer.Write(uvs?.Count ?? -1);
            if (uvs is null)
            {
                return;
            }

            foreach (ResoniteFloat2 uv in uvs)
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
        DemTerrainBounds? bounds = LocalCityGmlDemBootstrapSupport.ResolveDemTerrainBounds(
            demParsedSourceFiles.Select(global::PlateauResoniteLink.Application.Importing.ParsedSourceFileResult.FromLegacy),
            fallbackBounds is null ? null : DemTerrainBounds.FromLegacy(fallbackBounds));
        return bounds?.ToLegacy();
    }

    private static TerrainHeightTriangle[] ExtractTerrainHeightTriangles(
        IEnumerable<ParsedCityObject> cityObjects)
    {
        return LocalCityGmlDemBootstrapSupport.CreateTerrainHeightTriangles(
                cityObjects.Select(BootstrapParsedCityObject.FromLegacy))
            .Select(static triangle => triangle.ToLegacy())
            .ToArray();
    }

    private static ParsedCityObject ConformCityObjectToTerrain(
        ParsedCityObject parsedCityObject,
        TerrainHeightSampler? terrainHeightSampler)
    {
        if (terrainHeightSampler is null
            || !ShouldTerrainAlignCityObject(parsedCityObject))
        {
            return parsedCityObject;
        }

        ParsedCityObject subdividedCityObject = SubdivideTerrainAlignedCityObject(parsedCityObject);
        bool terrainAligned = false;
        GeodeticPoint cityObjectOrigin = GetCityObjectOrigin(subdividedCityObject);
        LocalCartesian? cityObjectCartesian = subdividedCityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                subdividedCityObject.ReferenceSystem.Geocentric)
            : null;
        ParsedSurface[] conformedSurfaces = PlateauPackageCatalog.IsRoadPackage(subdividedCityObject.PackageName)
            ? ConformRoadSurfacesToTerrainWithFallback(subdividedCityObject.Surfaces, terrainHeightSampler, ref terrainAligned)
            : ConformSurfacesToTerrain(
                subdividedCityObject.PackageName,
                subdividedCityObject.Surfaces,
                terrainHeightSampler,
                cityObjectOrigin,
                cityObjectCartesian,
                ref terrainAligned);

        return terrainAligned
            ? subdividedCityObject with
            {
                Surfaces = conformedSurfaces,
                TerrainAligned = true,
            }
            : subdividedCityObject;
    }

    private static ParsedCityObject SubdivideTerrainAlignedCityObject(ParsedCityObject cityObject)
    {
        if (!ShouldSubdivideTerrainAlignedCityObject(cityObject))
        {
            return cityObject;
        }

        List<ParsedSurface> subdividedSurfaces = [];
        foreach (ParsedSurface surface in cityObject.Surfaces)
        {
            subdividedSurfaces.AddRange(SubdivideTransportationSurfaceForTerrainAlignment(surface, cityObject));
        }

        return subdividedSurfaces.Count == cityObject.Surfaces.Length
            ? cityObject
            : cityObject with { Surfaces = subdividedSurfaces.ToArray() };
    }

    private static bool ShouldSubdivideTerrainAlignedCityObject(ParsedCityObject cityObject)
    {
        return PlateauPackageCatalog.IsRoadPackage(cityObject.PackageName)
            && (!cityObject.LodLevel.HasValue || cityObject.LodLevel.Value < 3);
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
        ResoniteFloat3[] positions = surface.ExteriorRing.Vertices
            .Select(point => CreateResonitePosition(point, cityObjectOrigin, cityObjectCartesian))
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

        List<ParsedSurface> strips = BuildTerrainAlignedTransportationStrips(surface, positions, edgePair, segmentLength);
        return strips.Count > 0 ? strips : [surface];
    }

    private static double ComputeTerrainAlignedSegmentLength(double roadWidth)
    {
        double preferredLength = roadWidth * TerrainAlignedTransportationSegmentLengthByWidthRatio;
        return Math.Clamp(
            preferredLength,
            MinTerrainAlignedTransportationSegmentLengthMeters,
            DefaultTerrainAlignedTransportationSegmentLengthMeters);
    }

    private static List<ParsedSurface> BuildTerrainAlignedTransportationStrips(
        ParsedSurface surface,
        ResoniteFloat3[] positions,
        EdgePairSelection edgePair,
        double segmentLength)
    {
        ResoniteFloat3 axis = CreateTransportationSurfaceAxis(edgePair);
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
        foreach (ResoniteFloat3 position in positions)
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
        ResoniteFloat2[]? uvs = null;
        if (samples.All(static sample => sample.UV is not null))
        {
            List<ResoniteFloat2> uvList = new(samples.Length);
            for (int index = 0; index < samples.Length; index++)
            {
                if (samples[index].UV is ResoniteFloat2 uv)
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
        ResoniteFloat3[] positions,
        ResoniteFloat3 axis,
        double station)
    {
        ResoniteFloat3 lateralAxis = new(-axis.Z, 0.0, axis.X);
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
        ResoniteFloat3[] positions,
        ResoniteFloat3 lateralAxis,
        int edgeStartIndex,
        GeodeticPoint point,
        double ratio)
    {
        if (intersections.Any(existing => AreSamePoint(existing.Point, point)))
        {
            return;
        }

        int edgeEndIndex = (edgeStartIndex + 1) % ring.Vertices.Length;
        ResoniteFloat3 position = Lerp(positions[edgeStartIndex], positions[edgeEndIndex], ratio);
        ResoniteFloat2? uv = ring.UVs is not null && ring.UVs.Count == ring.Vertices.Length
            ? Lerp(ring.UVs[edgeStartIndex], ring.UVs[edgeEndIndex], ratio)
            : null;
        intersections.Add(new SurfaceSliceSample(point, uv, DotHorizontal(position, lateralAxis)));
    }

    private static ResoniteFloat3 CreateTransportationSurfaceAxis(EdgePairSelection edgePair)
    {
        ResoniteFloat3 side0Vector = NormalizeHorizontal(Subtract(edgePair.Side0Positions[1], edgePair.Side0Positions[0]));
        ResoniteFloat3 side1Vector = NormalizeHorizontal(Subtract(edgePair.Side1Positions[1], edgePair.Side1Positions[0]));
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
            side1Vector = new ResoniteFloat3(-side1Vector.X, 0.0, -side1Vector.Z);
        }

        return NormalizeHorizontal(Add(side0Vector, side1Vector));
    }

    private static ResoniteFloat3 Add(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    private static ResoniteFloat3 NormalizeHorizontal(ResoniteFloat3 value)
    {
        double length = Math.Sqrt((value.X * value.X) + (value.Z * value.Z));
        if (length <= 1e-8)
        {
            return new ResoniteFloat3(0.0, 0.0, 0.0);
        }

        return new ResoniteFloat3(value.X / length, 0.0, value.Z / length);
    }

    private static double DotHorizontal(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return (left.X * right.X) + (left.Z * right.Z);
    }

    private static double LengthSquared(ResoniteFloat3 value)
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
        string packageName = cityObject.PackageName.ToLowerInvariant();
        if (PlateauPackageCatalog.IsRoadPackage(packageName))
        {
            return !cityObject.LodLevel.HasValue || cityObject.LodLevel.Value < 3;
        }

        return packageName switch
        {
            "fld" or "ifld" or "lsld" or "luse" or "rfld" or "tnm" or "urf" or "wtr" or "wwy" => true,
            _ => false,
        };
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

        ResoniteFloat3[] positions = surface.ExteriorRing.Vertices
            .Select(point => CreateResonitePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        return IsNearHorizontalSurface(positions);
    }

    private static bool IsNearHorizontalSurface(ResoniteFloat3[] positions)
    {
        ResoniteFloat3? normal = ComputePolygonNormal(positions);
        return normal is not null && Math.Abs(normal.Y) >= 0.7;
    }

    private static ParsedSurface? ParseSurface(XElement polygonElement, AppearanceLibrary appearanceLibrary)
    {
        XElement? exteriorRing = polygonElement
            .Element(Gml + "exterior")
            ?.Element(Gml + "LinearRing");
        if (exteriorRing is null)
        {
            return null;
        }

        string polygonId = GetAttribute(polygonElement, Gml + "id") ?? CreateStableElementId("polygon", polygonElement);
        SurfaceAppearance appearance = appearanceLibrary.Resolve(polygonId);
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
            BaseColor: appearance.BaseColor,
            TexturePayload: appearance.TexturePayload);
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
            BaseColor: appearance.BaseColor,
            TexturePayload: appearance.TexturePayload);
    }

    private static ParsedRing? ParseRing(
        XElement? ringElement,
        IReadOnlyDictionary<string, IReadOnlyList<ResoniteFloat2>>? ringUvsByRingId,
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

        IReadOnlyList<ResoniteFloat2>? uvs = null;
        if (ringUvsByRingId is not null
            && ringUvsByRingId.TryGetValue(ringId, out IReadOnlyList<ResoniteFloat2>? ringUvs)
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

    internal static ResoniteConstructionCityObject ProjectCityObject(
        ParsedCityObject cityObject,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        GeodeticPoint cityObjectOrigin = GetCityObjectOrigin(cityObject);

        LocalCartesian? cityObjectCartesian = cityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                cityObject.ReferenceSystem.Geocentric)
            : null;
        ResoniteFloat3 slotPosition = CreateResonitePosition(
            cityObjectOrigin,
            globalOriginPoint,
            globalCartesian);

        List<ResoniteMeshVertex> vertices = [];
        List<ResoniteMeshSubmesh> submeshes = [];
        List<ResoniteMaterialBinding> materials = [];
        GeographicRectangle? demObjectBounds = TryGetDemObjectGeographicBounds(cityObject, demTerrainTextureOverlay);
        DemUvProjection? demUvProjection = TryCreateDemUvProjection(demObjectBounds, cityObject, demTerrainTextureOverlay);

        List<ResolvedSurfaceMaterial> resolvedSurfaces =
        [
            .. cityObject.Surfaces.Select(surface => ResolveSurfaceMaterial(cityObject, cityObjectOrigin, cityObjectCartesian, surface, demTerrainTextureOverlay, materialResolver)),
        ];

        IGrouping<string, ResolvedSurfaceMaterial>[] materialGroups = resolvedSurfaces
            .GroupBy(
                resolvedSurface => CreateBindingMaterialKey(
                    resolvedSurface.Material,
                    resolvedSurface.DepthOffset,
                    resolvedSurface.Material.TextureScale,
                    resolvedSurface.Surface.BaseColor),
                StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToArray();

        for (int materialIndex = 0; materialIndex < materialGroups.Length; materialIndex++)
        {
            IGrouping<string, ResolvedSurfaceMaterial> materialGroup = materialGroups[materialIndex];
            List<int> indices = [];

            foreach (ResolvedSurfaceMaterial resolvedSurface in materialGroup
                         .OrderBy(static surface => CreateStableSurfaceSortKey(surface.Surface), StringComparer.Ordinal))
            {
                TriangulateSurface(
                    cityObject,
                    resolvedSurface.Surface,
                    resolvedSurface.Material,
                    cityObjectOrigin,
                    cityObjectCartesian,
                    globalOriginPoint,
                    globalCartesian,
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
            submeshes.Add(new ResoniteMeshSubmesh(materialIndex, materialGroup.Key, indices));
            materials.Add(
                new ResoniteMaterialBinding(
                    MaterialKey: materialGroup.Key,
                    BaseColor: representativeSurface.Surface.BaseColor,
                    MaterialType: representativeSurface.Material.MaterialType,
                    TexturePayload: representativeSurface.Material.TexturePayload,
                    TextureSourceKind: representativeSurface.Material.TextureSourceKind,
                    Projection: representativeSurface.Material.Projection,
                    DepthOffset: representativeSurface.DepthOffset,
                    SubmeshIndices: [materialIndex],
                    TextureScale: representativeSurface.Material.TextureScale,
                    Family: representativeSurface.Material.Family,
                    TextureOffset: null,
                    AssetScope: representativeSurface.Material.AssetScope,
                    TerrainOverlay: representativeSurface.Material.TerrainOverlay,
                    BundledVariantIndex: representativeSurface.Material.BundledVariantIndex));
        }

        return ResoniteDynamicMaterialUvNormalizer.Normalize(new ResoniteConstructionCityObject(
            SlotKey: cityObject.SlotKey,
            DisplayName: cityObject.DisplayName,
            PackageName: cityObject.PackageName,
            ActualMeshCode: cityObject.ActualMeshCode,
            LodLevel: cityObject.LodLevel,
            Transform: new ResoniteTransform(slotPosition),
            Mesh: new ResoniteImportedMesh(vertices, submeshes),
            Materials: materials,
            SourceObjectKey: cityObject.SourceIdentity,
            SourceUnitKey: cityObject.SourceUnitIdentity,
            SourceFileRelativePath: cityObject.SourceFileRelativePath));
    }

    private static GeodeticPoint GetCityObjectOrigin(ParsedCityObject cityObject)
    {
        if (cityObject.OriginOverride is not null)
        {
            return cityObject.OriginOverride;
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

    private static ResolvedSurfaceMaterial ResolveSurfaceMaterial(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        ParsedSurface surface,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        if (surface.UsesGeneratedDemTexture)
        {
            return new ResolvedSurfaceMaterial(
                surface,
                new ResolvedMaterial(
                    ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    ResoniteTextureSourceKind.Dataset,
                    ResoniteMaterialProjection.Uv,
                    Family: null,
                    TextureScale: null,
                    AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped,
                    TerrainOverlay: demTerrainTextureOverlay),
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
                        ResoniteMaterialType.VertexColor,
                        TexturePayload: null,
                        ResoniteTextureSourceKind.Bundled,
                        ResoniteMaterialProjection.Uv,
                        Family: null,
                        TextureScale: null,
                        AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped),
                    DepthOffset: null);
            }

            return new ResolvedSurfaceMaterial(
                surface with { BaseColor = DefaultVegetationMaterialColor },
                new ResolvedMaterial(
                    ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    ResoniteTextureSourceKind.Bundled,
                    ResoniteMaterialProjection.Uv,
                    Family: null,
                    TextureScale: null,
                    AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped),
                DepthOffset: null);
        }

        if (IsGeneratedRoadMarkingSurface(surface))
        {
            return new ResolvedSurfaceMaterial(
                surface,
                new ResolvedMaterial(
                    ResoniteMaterialType.VertexColor,
                    TexturePayload: null,
                    ResoniteTextureSourceKind.Bundled,
                    ResoniteMaterialProjection.Uv,
                    Family: null,
                    TextureScale: null,
                    AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped),
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
        ResoniteMaterialDepthOffset? depthOffset = cityObject.TerrainAligned
            ? DefaultTerrainAlignedMaterialDepthOffset
            : null;
        return new ResolvedSurfaceMaterial(surface, resolvedMaterial, depthOffset);
    }

    private static ParsedCityObject? CreateGeneratedRoadMarkingCityObject(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (!string.Equals(cityObject.PackageName, "tran", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        List<ParsedSurface> markingSurfaces = [];
        foreach (ParsedSurface surface in cityObject.Surfaces)
        {
            if (surface.TexturePayload is not null)
            {
                continue;
            }

            List<ParsedSurface> generatedSurfaces = CreateGeneratedRoadMarkingSurfaces(
                surface,
                cityObjectOrigin,
                cityObjectCartesian);
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
                SourceIdentity = $"{cityObject.SourceIdentity}_road_marking",
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

        ResoniteFloat3[] positions = vertices
            .Select(point => CreateResonitePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        ResoniteFloat3? normal = ComputePolygonNormal(positions);
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
            ResoniteFloat3[] side0Positions = side0Source
                .Select(point => CreateResonitePosition(point, cityObjectOrigin, cityObjectCartesian))
                .ToArray();
            ResoniteFloat3[] side1Positions = side1Source
                .Select(point => CreateResonitePosition(point, cityObjectOrigin, cityObjectCartesian))
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

    private static EdgePairSelection SelectPrimaryRoadEdgePair(
        GeodeticPoint[] vertices,
        ResoniteFloat3[] positions)
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
        ResoniteFloat3[] positions)
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
        ResoniteFloat3[] sourcePositions,
        ResoniteFloat3[] targetPositions,
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
        ParsedCityObject cityObject,
        ParsedSurface surface,
        ResolvedMaterial material,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        DemUvProjection? demUvProjection,
        List<ResoniteMeshVertex> vertices,
        List<int> indices)
    {
        List<(ResoniteMeshVertex First, ResoniteMeshVertex Second, ResoniteMeshVertex Third, string SortKey)> triangles = [];
        bool useVertexColors = material.MaterialType == ResoniteMaterialType.VertexColor;
        DemUvProjection? generatedDemUvProjection = surface.UsesGeneratedDemTexture ? demUvProjection : null;
        bool useGeneratedDemUv = generatedDemUvProjection is not null;
        SurfaceUvProjection? generatedSurfaceUvProjection = !useGeneratedDemUv
            && surface.TexturePayload is null
            && material.Projection == ResoniteMaterialProjection.Uv
                ? CreateGeneratedSurfaceUvProjection(
                    surface,
                    cityObject.PackageName,
                    cityObjectOrigin,
                    cityObjectCartesian)
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

        ResoniteFloat3? expectedNormal = ComputePolygonNormal(tessellatedRings[0].Vertices.Select(static vertex => vertex.Position));
        if (expectedNormal is null)
        {
            return;
        }

        (ResoniteFloat3 planeOrigin, ResoniteFloat3 basisX, ResoniteFloat3 basisY) = CreateSurfacePlane(tessellatedRings[0].Vertices);
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

            ResoniteFloat3 position0 = vertex0.Position;
            ResoniteFloat3 position1 = vertex1.Position;
            ResoniteFloat3 position2 = vertex2.Position;
            ResoniteFloat2 uv0 = vertex0.UV;
            ResoniteFloat2 uv1 = vertex1.UV;
            ResoniteFloat2 uv2 = vertex2.UV;
            ResoniteColor? color0 = vertex0.Color;
            ResoniteColor? color1 = vertex1.Color;
            ResoniteColor? color2 = vertex2.Color;

            ResoniteFloat3? triangleNormal = ComputeNormal(position0, position1, position2);
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

            ResoniteFloat3? resoniteNormal = ComputeNormal(position0, position2, position1);
            if (resoniteNormal is null)
            {
                continue;
            }

            if (string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
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

            (ResoniteMeshVertex first, ResoniteMeshVertex second, ResoniteMeshVertex third, string sortKey) =
                CreateCanonicalSurfaceTriangle(
                    new ResoniteMeshVertex(position0, resoniteNormal, uv0, color0),
                    new ResoniteMeshVertex(position1, resoniteNormal, uv1, color1),
                    new ResoniteMeshVertex(position2, resoniteNormal, uv2, color2));
            triangles.Add((first, second, third, sortKey));
        }

        triangles.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.SortKey, right.SortKey));
        foreach ((ResoniteMeshVertex first, ResoniteMeshVertex second, ResoniteMeshVertex third, _) in triangles)
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
        ResoniteMeshVertex First,
        ResoniteMeshVertex Second,
        ResoniteMeshVertex Third,
        string SortKey) CreateCanonicalSurfaceTriangle(
        ResoniteMeshVertex first,
        ResoniteMeshVertex second,
        ResoniteMeshVertex third)
    {
        (ResoniteMeshVertex First, ResoniteMeshVertex Second, ResoniteMeshVertex Third) best = (first, second, third);
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
        ResoniteMeshVertex first,
        ResoniteMeshVertex second,
        ResoniteMeshVertex third)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{CreateVertexSortKey(first)}|{CreateVertexSortKey(second)}|{CreateVertexSortKey(third)}");
    }

    private static string CreateVertexSortKey(ResoniteMeshVertex vertex)
    {
        ResoniteColor? color = vertex.Color;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{vertex.Position.X:R},{vertex.Position.Y:R},{vertex.Position.Z:R}|"
            + $"{vertex.Normal.X:R},{vertex.Normal.Y:R},{vertex.Normal.Z:R}|"
            + $"{vertex.UV0.X:R},{vertex.UV0.Y:R}|"
            + $"{color?.R ?? double.NaN:R},{color?.G ?? double.NaN:R},{color?.B ?? double.NaN:R},{color?.A ?? double.NaN:R}");
    }

    private static List<TessellatedRing> CreateSurfaceTessellatedRings(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        DemUvProjection? generatedDemUvProjection,
        SurfaceUvProjection? generatedSurfaceUvProjection,
        ResoniteColor? vertexColor)
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
        ResoniteColor? vertexColor)
    {
        TessellatedVertex[] vertices = ring.Vertices
            .Select((point, index) => new TessellatedVertex(
                CreateResonitePosition(point, cityObjectOrigin, cityObjectCartesian),
                ring.UVs is not null && index < ring.UVs.Count
                    ? ring.UVs[index]
                    : generatedDemUvProjection is not null
                        ? CreateGeneratedDemUv(point, generatedDemUvProjection.Value)
                        : generatedSurfaceUvProjection is not null
                            ? CreateGeneratedSurfaceUv(point, cityObjectOrigin, cityObjectCartesian, generatedSurfaceUvProjection)
                            : new ResoniteFloat2(0.0, 0.0),
                vertexColor))
            .ToArray();
        return new TessellatedRing(ring.RingId, vertices);
    }

    private static ResoniteFloat2 CreateGeneratedDemUv(
        GeodeticPoint point,
        DemUvProjection demUvProjection)
    {
        double pointX = WebMercatorTileMath.LongitudeToNormalizedX(point.Longitude);
        double pointY = WebMercatorTileMath.LatitudeToNormalizedY(point.Latitude);

        return new ResoniteFloat2(
            Math.Clamp((pointX - demUvProjection.West) / demUvProjection.Width, 0.0, 1.0),
            Math.Clamp((demUvProjection.South - pointY) / demUvProjection.Height, 0.0, 1.0));
    }

    private static DemUvProjection? TryCreateDemUvProjection(
        GeographicRectangle? cityObjectBounds,
        ParsedCityObject cityObject,
        TerrainTextureOverlay? demTerrainTextureOverlay)
    {
        if (cityObjectBounds is null
            || demTerrainTextureOverlay is null
            || !string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        GeographicRectangle objectBounds = IntersectGeographicBounds(
            cityObjectBounds,
            demTerrainTextureOverlay.GeographicBounds);
        double west = WebMercatorTileMath.LongitudeToNormalizedX(objectBounds.MinLongitude);
        double east = WebMercatorTileMath.LongitudeToNormalizedX(objectBounds.MaxLongitude);
        double north = WebMercatorTileMath.LatitudeToNormalizedY(objectBounds.MaxLatitude);
        double south = WebMercatorTileMath.LatitudeToNormalizedY(objectBounds.MinLatitude);
        double width = Math.Max(east - west, 1e-12);
        double height = Math.Max(south - north, 1e-12);

        return new DemUvProjection(west, south, width, height);
    }

    private static DemUvProjection? TryCreateDemUvProjection(
        ParsedCityObject cityObject,
        ParsedSurface surface,
        TerrainTextureOverlay? demTerrainTextureOverlay)
    {
        if (demTerrainTextureOverlay is null
            || !string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            || !surface.UsesGeneratedDemTexture)
        {
            return null;
        }

        return TryCreateDemUvProjection(GetCityObjectGeographicBounds(cityObject), cityObject, demTerrainTextureOverlay);
    }

    private static SurfaceUvProjection? CreateGeneratedSurfaceUvProjection(
        ParsedSurface surface,
        string packageName,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        ResoniteFloat3[] positions = surface.ExteriorRing.Vertices
            .Select(point => CreateResonitePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        if (positions.Length < 3)
        {
            return null;
        }

        ResoniteFloat3? normal = ComputePolygonNormal(positions);
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

        return new SurfaceUvProjection(surfaceAxes.AxisU, surfaceAxes.AxisV);
    }

    private static ResoniteFloat2 CreateGeneratedSurfaceUv(
        GeodeticPoint point,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        SurfaceUvProjection projection)
    {
        ResoniteFloat3 position = CreateResonitePosition(point, cityObjectOrigin, cityObjectCartesian);
        double u = Dot(position, projection.AxisU);
        double v = Dot(position, projection.AxisV);
        return new ResoniteFloat2(u, v);
    }

    private static SurfaceUvAxes? TryCreateSurfaceUvAxes(ResoniteFloat3 normal)
    {
        ResoniteFloat3 verticalAxis = new(0.0, 1.0, 0.0);
        ResoniteFloat3 facadeAxisU = Cross(verticalAxis, normal);
        if (Magnitude(facadeAxisU) >= 1e-8)
        {
            return new SurfaceUvAxes(Normalize(facadeAxisU), verticalAxis);
        }

        ResoniteFloat3[] referenceAxes =
        [
            new ResoniteFloat3(1.0, 0.0, 0.0),
            new ResoniteFloat3(0.0, 0.0, 1.0),
            verticalAxis,
        ];

        foreach (ResoniteFloat3 referenceAxis in referenceAxes.OrderBy(axis => Math.Abs(Dot(normal, axis))))
        {
            ResoniteFloat3 axisU = Cross(referenceAxis, normal);
            if (Magnitude(axisU) < 1e-8)
            {
                continue;
            }

            axisU = Normalize(axisU);
            ResoniteFloat3 axisV = Cross(normal, axisU);
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
        ResoniteFloat3[] positions,
        ResoniteFloat3 normal)
    {
        if (!PlateauPackageCatalog.IsPathLikePackage(packageName)
            || positions.Length < 2
            || Math.Abs(normal.Y) < 0.7)
        {
            return null;
        }

        ResoniteFloat3 axisU = Subtract(positions[1], positions[0]);
        double axisULength = 0.0;
        for (int index = 0; index < positions.Length; index++)
        {
            ResoniteFloat3 start = positions[index];
            ResoniteFloat3 end = positions[(index + 1) % positions.Length];
            ResoniteFloat3 edge = Subtract(end, start);
            ResoniteFloat3 planarEdge = Subtract(edge, Multiply(normal, Dot(edge, normal)));
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
        ResoniteFloat3 axisV = Cross(normal, axisU);
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

    private static (ResoniteFloat3 Origin, ResoniteFloat3 BasisX, ResoniteFloat3 BasisY) CreateSurfacePlane(
        IReadOnlyList<TessellatedVertex> vertices)
    {
        ResoniteFloat3 origin = vertices[0].Position;
        ResoniteFloat3? normal = ComputePolygonNormal(vertices.Select(static vertex => vertex.Position))
            ?? throw new PlateauImportValidationException(["Failed to resolve a polygon plane for tessellation."]);

        ResoniteFloat3? basisX = null;
        foreach (TessellatedVertex vertex in vertices.Skip(1))
        {
            ResoniteFloat3 candidate = Subtract(vertex.Position, origin);
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

        ResoniteFloat3 basisY = Normalize(Cross(normal, basisX));
        return (origin, basisX, basisY);
    }

    private static ContourVertex CreateContourVertex(
        TessellatedVertex vertex,
        ResoniteFloat3 planeOrigin,
        ResoniteFloat3 basisX,
        ResoniteFloat3 basisY)
    {
        ResoniteFloat3 delta = Subtract(vertex.Position, planeOrigin);
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
            new ResoniteFloat3(x, y, z),
            new ResoniteFloat2(u, v),
            hasColor ? new ResoniteColor(r, g, b, a) : null);
    }

    private static TessVertexPayload GetTessVertexPayload(Tess tessellator, int elementIndex)
    {
        return tessellator.Vertices[elementIndex].Data as TessVertexPayload
            ?? throw new PlateauImportValidationException(["Polygon tessellation produced a vertex without payload data."]);
    }

    private static ResoniteFloat3? ComputePolygonNormal(IEnumerable<ResoniteFloat3> positions)
    {
        ResoniteFloat3[] points = positions.ToArray();
        if (points.Length < 3)
        {
            return null;
        }

        double normalX = 0.0;
        double normalY = 0.0;
        double normalZ = 0.0;

        for (int index = 0; index < points.Length; index++)
        {
            ResoniteFloat3 current = points[index];
            ResoniteFloat3 next = points[(index + 1) % points.Length];
            normalX += (current.Y - next.Y) * (current.Z + next.Z);
            normalY += (current.Z - next.Z) * (current.X + next.X);
            normalZ += (current.X - next.X) * (current.Y + next.Y);
        }

        double magnitude = Math.Sqrt((normalX * normalX) + (normalY * normalY) + (normalZ * normalZ));
        if (magnitude < 1e-8)
        {
            return null;
        }

        return new ResoniteFloat3(normalX / magnitude, normalY / magnitude, normalZ / magnitude);
    }

    private static ResoniteFloat3? ComputeNormal(
        ResoniteFloat3 position0,
        ResoniteFloat3 position1,
        ResoniteFloat3 position2)
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

        return new ResoniteFloat3(crossX / magnitude, crossY / magnitude, crossZ / magnitude);
    }

    private static ResoniteFloat3 Subtract(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(
            left.X - right.X,
            left.Y - right.Y,
            left.Z - right.Z);
    }

    private static ResoniteFloat3 Multiply(ResoniteFloat3 vector, double scalar)
    {
        return new ResoniteFloat3(
            vector.X * scalar,
            vector.Y * scalar,
            vector.Z * scalar);
    }

    private static ResoniteFloat3 Cross(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));
    }

    private static double Dot(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }

    private static double Magnitude(ResoniteFloat3 vector)
    {
        return Math.Sqrt(Dot(vector, vector));
    }

    private static double Distance(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return Math.Sqrt(DistanceSquared(left, right));
    }

    private static double DistanceSquared(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        double deltaX = left.X - right.X;
        double deltaY = left.Y - right.Y;
        double deltaZ = left.Z - right.Z;
        return (deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ);
    }

    private static ResoniteFloat3 Normalize(ResoniteFloat3 vector)
    {
        double magnitude = Magnitude(vector);
        if (magnitude < 1e-8)
        {
            throw new PlateauImportValidationException(["Attempted to normalize a zero-length polygon vector."]);
        }

        return new ResoniteFloat3(
            vector.X / magnitude,
            vector.Y / magnitude,
            vector.Z / magnitude);
    }

    private static ResoniteFloat3 MapToResonitePosition((double x, double y, double z) eun)
    {
        return new ResoniteFloat3(
            X: eun.x,
            Y: eun.z,
            Z: eun.y);
    }

    private static ResoniteFloat3 CreateResonitePosition(
        GeodeticPoint point,
        GeodeticPoint origin,
        LocalCartesian? cartesian)
    {
        if (cartesian is null)
        {
            return new ResoniteFloat3(
                X: point.Latitude - origin.Latitude,
                Y: point.Altitude - origin.Altitude,
                Z: point.Longitude - origin.Longitude);
        }

        return MapToResonitePosition(cartesian.Forward(
            point.Latitude,
            point.Longitude,
            point.Altitude));
    }

    private static string CreateMaterialKey(
        ResoniteMaterialType materialType,
        TerrainTextureOverlay? terrainOverlay,
        ResoniteTexturePayload? texturePayload,
        ResoniteTextureSourceKind textureSourceKind,
        ResoniteMaterialProjection projection,
        ResoniteMaterialDepthOffset? depthOffset,
        ResoniteFloat2? textureScale,
        string? family,
        ResoniteColor color,
        ResoniteFloat2? textureOffset = null)
    {
        string colorKey = string.Create(
            CultureInfo.InvariantCulture,
            $"{color.R:0.######},{color.G:0.######},{color.B:0.######},{color.A:0.######}");
        string depthOffsetKey = depthOffset is null
            ? "none"
            : string.Create(CultureInfo.InvariantCulture, $"{depthOffset.Factor:0.######},{depthOffset.Units:0.######}");
        string textureScaleKey = textureScale is null
            ? "none"
            : string.Create(CultureInfo.InvariantCulture, $"{textureScale.X:0.######},{textureScale.Y:0.######}");
        string textureOffsetKey = textureOffset is null
            ? "none"
            : string.Create(CultureInfo.InvariantCulture, $"{textureOffset.X:0.######},{textureOffset.Y:0.######}");
        string familyKey = string.IsNullOrWhiteSpace(family)
            ? "none"
            : family.ToLowerInvariant();

        string materialTypeKey = materialType.ToString().ToLowerInvariant();
        if (terrainOverlay is not null)
        {
            return $"{materialTypeKey}|{projection.ToString().ToLowerInvariant()}|terrain-overlay:{CreateTerrainOverlayToken(terrainOverlay)}|family:{familyKey}|depth:{depthOffsetKey}|scale:{textureScaleKey}|offset:{textureOffsetKey}|color:{colorKey}";
        }

        return texturePayload is null
            ? $"type:{materialTypeKey}|family:{familyKey}|depth:{depthOffsetKey}|scale:{textureScaleKey}|offset:{textureOffsetKey}|color:{colorKey}"
            : $"{materialTypeKey}|{projection.ToString().ToLowerInvariant()}|{textureSourceKind.ToString().ToLowerInvariant()}-texture:{texturePayload.Identity ?? "payload"}|family:{familyKey}|depth:{depthOffsetKey}|scale:{textureScaleKey}|offset:{textureOffsetKey}|color:{colorKey}";
    }

    private static string CreateBindingMaterialKey(
        ResolvedMaterial material,
        ResoniteMaterialDepthOffset? depthOffset,
        ResoniteFloat2? textureScale,
        ResoniteColor color,
        ResoniteFloat2? textureOffset = null)
    {
        if (material.AssetScope == ResoniteMaterialAssetScope.Common)
        {
            string family = material.Family ?? throw new InvalidOperationException("Common material must provide a family.");
            int variantIndex = material.BundledVariantIndex ?? 0;
            return string.Create(CultureInfo.InvariantCulture, $"common|{family}|variant:{variantIndex}|{material.Projection}");
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

    private static string CreateTerrainOverlayToken(TerrainTextureOverlay terrainOverlay)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{terrainOverlay.PackageName.ToLowerInvariant()}|{terrainOverlay.SourceIdentityKey}|"
            + $"{terrainOverlay.GeographicBounds.MinLatitude:0.######},"
            + $"{terrainOverlay.GeographicBounds.MaxLatitude:0.######},"
            + $"{terrainOverlay.GeographicBounds.MinLongitude:0.######},"
            + $"{terrainOverlay.GeographicBounds.MaxLongitude:0.######}");
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
        ResoniteFloat3[] positions = surface.Vertices
            .Select(point => CreateResonitePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        ResoniteFloat3? normal = ComputePolygonNormal(positions);
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
        ResoniteFloat3[] positions = surface.Vertices
            .Select(point => CreateResonitePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        ResoniteFloat3? normal = ComputePolygonNormal(positions);
        return normal is not null && Math.Abs(normal.Y) >= 0.98;
    }

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

    internal static List<ResoniteFloat2> ParseTextureCoordinates(string value)
    {
        double[] ordinates = ParseDoubles(value);
        List<ResoniteFloat2> coordinates = [];
        for (int index = 0; index + 1 < ordinates.Length; index += 2)
        {
            coordinates.Add(new ResoniteFloat2(ordinates[index], ordinates[index + 1]));
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

    private static ResoniteFloat3 Lerp(ResoniteFloat3 source, ResoniteFloat3 target, double ratio)
    {
        return new ResoniteFloat3(
            source.X + ((target.X - source.X) * ratio),
            source.Y + ((target.Y - source.Y) * ratio),
            source.Z + ((target.Z - source.Z) * ratio));
    }

    private static ResoniteFloat2 Lerp(ResoniteFloat2 source, ResoniteFloat2 target, double ratio)
    {
        return new ResoniteFloat2(
            source.X + ((target.X - source.X) * ratio),
            source.Y + ((target.Y - source.Y) * ratio));
    }

    private static bool AreSameUV(ResoniteFloat2 left, ResoniteFloat2 right)
    {
        return Math.Abs(left.X - right.X) < 1e-8
            && Math.Abs(left.Y - right.Y) < 1e-8;
    }

    private static bool HasExplicitMaterialColor(ResoniteColor color)
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

    internal static IEnumerable<ResoniteConstructionCityObject> ProjectCityObjects(
        CachedSourceFileDescriptor sourceFile,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
        TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver,
        Func<ParsedCityObject, bool>? predicate = null)
    {
        ValidateCompatibleReferenceSystem(
            referenceSystem,
            sourceFile.CityObjects.FirstOrDefault()?.ReferenceSystem ?? referenceSystem);

        foreach (ParsedCityObject parsedCityObject in sourceFile.CityObjects)
        {
            if (predicate is not null && !predicate(parsedCityObject))
            {
                continue;
            }

            foreach (ResoniteConstructionCityObject cityObject in ProjectParsedCityObject(
                         parsedCityObject,
                         globalOriginPoint,
                         globalCartesian,
                         demTerrainTextureOverlays,
                         requestedMeshAreas,
                         terrainHeightSampler,
                         request,
                         materialResolver))
            {
                yield return cityObject;
            }
        }
    }

    internal static IEnumerable<ResoniteMaterialBinding> EnumerateCommonMaterials(
        CachedSourceFileDescriptor sourceFile,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas,
        TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver,
        ISet<string>? emittedMaterialKeys = null)
    {
        ValidateCompatibleReferenceSystem(
            referenceSystem,
            sourceFile.CityObjects.FirstOrDefault()?.ReferenceSystem ?? referenceSystem);

        foreach (ParsedCityObject parsedCityObject in sourceFile.CityObjects)
        {
            foreach (ResoniteMaterialBinding material in EnumerateCommonMaterialsForParsedCityObject(
                         parsedCityObject,
                         globalOriginPoint,
                         globalCartesian,
                         demTerrainTextureOverlays,
                         requestedMeshAreas,
                         terrainHeightSampler,
                         request,
                         materialResolver))
            {
                if (emittedMaterialKeys is not null && !emittedMaterialKeys.Add(material.MaterialKey))
                {
                    continue;
                }

                yield return material;
            }
        }
    }

    internal static IEnumerable<ResoniteConstructionCityObject> ProjectParsedCityObject(
        ParsedCityObject parsedCityObject,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
        TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver)
    {
        ParsedCityObject terrainAlignedCityObject = ConformCityObjectToTerrain(parsedCityObject, terrainHeightSampler);
        List<ResoniteConstructionCityObject> projectedCityObjects = [];
        List<ResoniteConstructionCityObject> generatedRoadMarkings = [];

        foreach ((ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) splitCityObject in SplitParsedCityObject(
                     terrainAlignedCityObject,
                     demTerrainTextureOverlays,
                     requestedMeshAreas))
        {
            ResoniteConstructionCityObject cityObject = request.DemTerrainMode == DemTerrainMode.HeightMap
                && string.Equals(splitCityObject.CityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
                && TryProjectDemHeightMapCityObject(
                    splitCityObject.CityObject,
                    globalOriginPoint,
                    globalCartesian,
                    splitCityObject.Overlay,
                    request,
                    materialResolver,
                    out ResoniteConstructionCityObject? heightMapCityObject)
                    ? heightMapCityObject!
                    : ProjectCityObject(
                        splitCityObject.CityObject,
                        globalOriginPoint,
                        globalCartesian,
                        splitCityObject.Overlay,
                        materialResolver);

            if (HasRenderableGeometry(cityObject))
            {
                projectedCityObjects.Add(cityObject);
            }

            GeodeticPoint markingOrigin = GetCityObjectOrigin(splitCityObject.CityObject);
            LocalCartesian? markingCartesian = splitCityObject.CityObject.ReferenceSystem.IsGeographic
                ? new LocalCartesian(
                    markingOrigin.Latitude,
                    markingOrigin.Longitude,
                    markingOrigin.Altitude,
                    splitCityObject.CityObject.ReferenceSystem.Geocentric)
                : null;
            ParsedCityObject? roadMarkingCityObject = CreateGeneratedRoadMarkingCityObject(
                splitCityObject.CityObject,
                markingOrigin,
                markingCartesian);
            if (roadMarkingCityObject is null)
            {
                continue;
            }

            ResoniteConstructionCityObject markingObject = ProjectCityObject(
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

        ResoniteConstructionCityObject[] alignedCityObjects =
            request.DemTerrainMode == DemTerrainMode.HeightMap
            && string.Equals(parsedCityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
                ? AlignAdjacentDemHeightMapChunkBoundaries(projectedCityObjects)
                : [.. projectedCityObjects];

        foreach (ResoniteConstructionCityObject cityObject in alignedCityObjects)
        {
            yield return cityObject;
        }

        foreach (ResoniteConstructionCityObject markingObject in generatedRoadMarkings)
        {
            yield return markingObject;
        }
    }

    internal static IEnumerable<ResoniteMaterialBinding> EnumerateCommonMaterialsForParsedCityObject(
        ParsedCityObject parsedCityObject,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas,
        TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver)
    {
        ParsedCityObject terrainAlignedCityObject = ConformCityObjectToTerrain(parsedCityObject, terrainHeightSampler);

        foreach ((ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) splitCityObject in SplitParsedCityObject(
                     terrainAlignedCityObject,
                     demTerrainTextureOverlays,
                     requestedMeshAreas))
        {
            GeodeticPoint cityObjectOrigin = GetCityObjectOrigin(splitCityObject.CityObject);
            LocalCartesian? cityObjectCartesian = splitCityObject.CityObject.ReferenceSystem.IsGeographic
                ? new LocalCartesian(
                    cityObjectOrigin.Latitude,
                    cityObjectOrigin.Longitude,
                    cityObjectOrigin.Altitude,
                    splitCityObject.CityObject.ReferenceSystem.Geocentric)
                : null;

            foreach (ResoniteMaterialBinding material in request.DemTerrainMode == DemTerrainMode.HeightMap
                         && string.Equals(splitCityObject.CityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
                            ? CreateDemHeightMapMaterials(
                                splitCityObject.CityObject,
                                cityObjectOrigin,
                                cityObjectCartesian,
                                splitCityObject.Overlay,
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

    private static ResoniteMaterialBinding[] CreateCommonMaterialBindings(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        List<ResolvedSurfaceMaterial> resolvedSurfaces =
        [
            .. cityObject.Surfaces.Select(surface => ResolveSurfaceMaterial(cityObject, cityObjectOrigin, cityObjectCartesian, surface, demTerrainTextureOverlay, materialResolver)),
        ];

        return resolvedSurfaces
            .GroupBy(
                static resolvedSurface => CreateBindingMaterialKey(
                    resolvedSurface.Material,
                    resolvedSurface.DepthOffset,
                    resolvedSurface.Material.TextureScale,
                    resolvedSurface.Surface.BaseColor),
                StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select((group, materialIndex) =>
            {
                ResolvedSurfaceMaterial representativeSurface = group.First();
                return new ResoniteMaterialBinding(
                    MaterialKey: CreateBindingMaterialKey(
                        representativeSurface.Material,
                        representativeSurface.DepthOffset,
                        representativeSurface.Material.TextureScale,
                        representativeSurface.Surface.BaseColor),
                    BaseColor: representativeSurface.Surface.BaseColor,
                    MaterialType: representativeSurface.Material.MaterialType,
                    TexturePayload: representativeSurface.Material.TexturePayload,
                    TextureSourceKind: representativeSurface.Material.TextureSourceKind,
                    Projection: representativeSurface.Material.Projection,
                    DepthOffset: representativeSurface.DepthOffset,
                    SubmeshIndices: [materialIndex],
                    TextureScale: representativeSurface.Material.TextureScale,
                    Family: representativeSurface.Material.Family,
                    AssetScope: representativeSurface.Material.AssetScope,
                    TerrainOverlay: representativeSurface.Material.TerrainOverlay,
                    BundledVariantIndex: representativeSurface.Material.BundledVariantIndex);
            })
            .Where(static material => material.AssetScope == ResoniteMaterialAssetScope.Common)
            .ToArray();
    }

    private static ResoniteConstructionCityObject[] AlignAdjacentDemHeightMapChunkBoundaries(
        IReadOnlyList<ResoniteConstructionCityObject> cityObjects)
    {
        HeightMapChunkAlignmentState?[] states = cityObjects
            .Select(static cityObject => HeightMapChunkAlignmentState.TryCreate(cityObject))
            .ToArray();
        if (states.Any(static state => state is null))
        {
            return cityObjects.ToArray();
        }

        HeightMapChunkAlignmentState[] chunkStates = states
            .Select(static state => state!)
            .ToArray();
        const double seaLevelWorldHeightTolerance = 1e-6;
        Dictionary<DemBoundarySampleKey, List<BoundaryHeightSampleReference>> sampleReferencesByKey = [];
        foreach (HeightMapChunkAlignmentState state in chunkStates)
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
                || references.Select(static reference => reference.State.CityObject.SourceObjectKey ?? reference.State.CityObject.SlotKey).Distinct(StringComparer.Ordinal).Count() < 2)
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
        HeightMapChunkAlignmentState state)
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
        HeightMapChunkAlignmentState state,
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

    private sealed class HeightMapChunkAlignmentState
    {
        public HeightMapChunkAlignmentState(
            ResoniteConstructionCityObject cityObject,
            ResoniteHeightMapGridGeometry geometry,
            double[] heightSamples)
        {
            CityObject = cityObject;
            Geometry = geometry;
            HeightSamples = heightSamples;
            BaseHeight = cityObject.Transform.Position.Y - geometry.MaxHeight;
        }

        public ResoniteConstructionCityObject CityObject { get; }

        public ResoniteHeightMapGridGeometry Geometry { get; }

        public double[] HeightSamples { get; }

        public double BaseHeight { get; }

        public static HeightMapChunkAlignmentState? TryCreate(ResoniteConstructionCityObject cityObject)
        {
            return cityObject.Geometry is ResoniteHeightMapGridGeometry geometry
                ? new HeightMapChunkAlignmentState(cityObject, geometry, geometry.HeightSamples.ToArray())
                : null;
        }

        public ResoniteConstructionCityObject ToCityObject()
        {
            double minHeight = HeightSamples.Min();
            double maxHeight = HeightSamples.Max();

            return CityObject with
            {
                Transform = CityObject.Transform with
                {
                    Position = CityObject.Transform.Position with
                    {
                        Y = BaseHeight + maxHeight,
                    },
                },
                Geometry = Geometry with
                {
                    MinHeight = minHeight,
                    MaxHeight = maxHeight,
                    HeightSamples = HeightSamples,
                },
            };
        }
    }

    private sealed record DemBoundarySampleKey(
        long QuantizedX,
        long QuantizedZ);

    private sealed record BoundaryHeightSampleReference(
        HeightMapChunkAlignmentState State,
        int SampleIndex,
        DemBoundarySampleKey Key);

    private static bool TryProjectDemHeightMapCityObject(
        ParsedCityObject cityObject,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver,
        out ResoniteConstructionCityObject? heightMapCityObject)
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

        ResoniteFloat3 slotPosition = CreateResonitePosition(cityObjectOrigin, globalOriginPoint, globalCartesian);
        ResoniteFloat3[] positions = cityObject.Surfaces
            .SelectMany(static surface => surface.Vertices)
            .Select(point => CreateGlobalHeightMapLocalPosition(point, slotPosition, globalOriginPoint, globalCartesian))
            .ToArray();
        HeightMapTriangle[] triangles = CreateDemHeightMapTriangles(cityObject, slotPosition, globalOriginPoint, globalCartesian);
        double seaLevelLocalHeight = CreateGlobalHeightMapLocalPosition(
            new GeodeticPoint(cityObjectOrigin.Latitude, cityObjectOrigin.Longitude, 0.0),
            slotPosition,
            globalOriginPoint,
            globalCartesian).Y;
        if (positions.Length < 3)
        {
            return false;
        }

        DemHeightMapBounds heightMapBounds = CreateDemHeightMapBounds(
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

        HeightMapSpatialIndex spatialIndex = HeightMapSpatialIndex.Create(
            triangles,
            minX,
            maxX,
            minZ,
            maxZ);

        int width = Math.Clamp(
            (int)Math.Ceiling(extentX / request.DemHeightmapMetersPerVertex) + 1,
            2,
            request.DemHeightmapMaxResolution);
        int height = Math.Clamp(
            (int)Math.Ceiling(extentZ / request.DemHeightmapMetersPerVertex) + 1,
            2,
            request.DemHeightmapMaxResolution);
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

        ResoniteMaterialBinding[] materials = CreateDemHeightMapMaterials(
            cityObject,
            cityObjectOrigin,
            cityObjectCartesian,
            demTerrainTextureOverlay,
            materialResolver);
        if (materials.Length == 0)
        {
            return false;
        }

        ResoniteFloat3 adjustedSlotPosition = slotPosition with
        {
            // Elements.Assets.Grid centers vertices in-plane, so split DEM chunks need their own bbox-center offset here.
            X = slotPosition.X + centerX,
            // GridMesh displaces the inverted heightmap downward in world Y, so the slot must start at the patch-local maximum height.
            Y = slotPosition.Y + maxHeight,
            Z = slotPosition.Z + centerZ,
        };

        heightMapCityObject = new ResoniteConstructionCityObject(
            SlotKey: cityObject.SlotKey,
            DisplayName: cityObject.DisplayName,
            PackageName: cityObject.PackageName,
            ActualMeshCode: cityObject.ActualMeshCode,
            LodLevel: cityObject.LodLevel,
            Transform: new ResoniteTransform(
                adjustedSlotPosition,
                GridMeshTerrainRotation),
            Geometry: new ResoniteHeightMapGridGeometry(
                Width: width,
                Height: height,
                Size: new ResoniteFloat2(extentX, extentZ),
                MinHeight: minHeight,
                MaxHeight: maxHeight,
                HeightSamples: localHeights),
            Materials: materials,
            SourceObjectKey: cityObject.SourceIdentity,
            SourceUnitKey: cityObject.SourceUnitIdentity,
            SourceFileRelativePath: cityObject.SourceFileRelativePath);
        return true;
    }

    private static ResoniteMaterialBinding[] CreateDemHeightMapMaterials(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        GeographicRectangle? demObjectBounds = TryGetDemObjectGeographicBounds(cityObject, demTerrainTextureOverlay);
        List<ResolvedSurfaceMaterial> resolvedSurfaces =
        [
            .. cityObject.Surfaces.Select(surface => ResolveSurfaceMaterial(cityObject, cityObjectOrigin, cityObjectCartesian, surface, demTerrainTextureOverlay, materialResolver)),
        ];

        return resolvedSurfaces
            .GroupBy(
                resolvedSurface =>
                {
                    (ResoniteFloat2? textureScale, ResoniteFloat2? textureOffset) =
                        DemTerrainOverlayAssignment.TryCreateHeightMapTextureTransform(cityObject, resolvedSurface, demTerrainTextureOverlay, demObjectBounds);
                    return CreateBindingMaterialKey(
                        resolvedSurface.Material,
                        resolvedSurface.DepthOffset,
                        textureScale ?? resolvedSurface.Material.TextureScale,
                        resolvedSurface.Surface.BaseColor,
                        textureOffset);
                },
                StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select((group, materialIndex) =>
            {
                ResolvedSurfaceMaterial representativeSurface = group.First();
                (ResoniteFloat2? textureScale, ResoniteFloat2? textureOffset) =
                    DemTerrainOverlayAssignment.TryCreateHeightMapTextureTransform(cityObject, representativeSurface, demTerrainTextureOverlay, demObjectBounds);
                return new ResoniteMaterialBinding(
                    MaterialKey: CreateBindingMaterialKey(
                        representativeSurface.Material,
                        representativeSurface.DepthOffset,
                        textureScale ?? representativeSurface.Material.TextureScale,
                        representativeSurface.Surface.BaseColor,
                        textureOffset),
                    BaseColor: representativeSurface.Surface.BaseColor,
                    MaterialType: representativeSurface.Material.MaterialType,
                    TexturePayload: representativeSurface.Material.TexturePayload,
                    TextureSourceKind: representativeSurface.Material.TextureSourceKind,
                    Projection: representativeSurface.Material.Projection,
                    DepthOffset: representativeSurface.DepthOffset,
                    SubmeshIndices: [materialIndex],
                    TextureScale: textureScale ?? representativeSurface.Material.TextureScale,
                    Family: representativeSurface.Material.Family,
                    TextureOffset: textureOffset,
                    AssetScope: representativeSurface.Material.AssetScope,
                    TerrainOverlay: representativeSurface.Material.TerrainOverlay,
                    BundledVariantIndex: representativeSurface.Material.BundledVariantIndex);
            })
            .ToArray();
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

    private static DemHeightMapBounds CreateDemHeightMapBounds(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        ResoniteFloat3 slotPosition,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IReadOnlyList<ResoniteFloat3> positions)
    {
        double rawMinX = positions.Min(static position => position.X);
        double rawMaxX = positions.Max(static position => position.X);
        double rawMinZ = positions.Min(static position => position.Z);
        double rawMaxZ = positions.Max(static position => position.Z);

        if (demTerrainTextureOverlay is null)
        {
            return new DemHeightMapBounds(rawMinX, rawMaxX, rawMinZ, rawMaxZ);
        }

        GeographicRectangle clippedBounds = IntersectGeographicBounds(
            GetCityObjectGeographicBounds(cityObject),
            demTerrainTextureOverlay.GeographicBounds);
        double referenceLatitude = cityObjectOrigin.Latitude;
        double referenceLongitude = cityObjectOrigin.Longitude;
        ResoniteFloat3 westPosition = CreateGlobalHeightMapLocalPosition(
            new GeodeticPoint(referenceLatitude, clippedBounds.MinLongitude, cityObjectOrigin.Altitude),
            slotPosition,
            globalOriginPoint,
            globalCartesian);
        ResoniteFloat3 eastPosition = CreateGlobalHeightMapLocalPosition(
            new GeodeticPoint(referenceLatitude, clippedBounds.MaxLongitude, cityObjectOrigin.Altitude),
            slotPosition,
            globalOriginPoint,
            globalCartesian);
        ResoniteFloat3 southPosition = CreateGlobalHeightMapLocalPosition(
            new GeodeticPoint(clippedBounds.MinLatitude, referenceLongitude, cityObjectOrigin.Altitude),
            slotPosition,
            globalOriginPoint,
            globalCartesian);
        ResoniteFloat3 northPosition = CreateGlobalHeightMapLocalPosition(
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
            return new DemHeightMapBounds(rawMinX, rawMaxX, rawMinZ, rawMaxZ);
        }

        return new DemHeightMapBounds(clippedMinX, clippedMaxX, clippedMinZ, clippedMaxZ);
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

    private static ResoniteFloat3 TransformPointToWorld(ResoniteTransform transform, ResoniteFloat3 localPosition)
    {
        ResoniteFloat3 rotated = transform.Rotation is null
            ? localPosition
            : Rotate(localPosition, transform.Rotation);
        return Add(transform.Position, rotated);
    }

    private static ResoniteFloat3 Rotate(ResoniteFloat3 value, ResoniteFloatQ rotation)
    {
        ResoniteFloat3 qv = new(rotation.X, rotation.Y, rotation.Z);
        ResoniteFloat3 uv = Cross3(qv, value);
        ResoniteFloat3 uuv = Cross3(qv, uv);
        return Add(
            value,
            Add(
                Scale3(uv, 2.0 * rotation.W),
                Scale3(uuv, 2.0)));
    }

    private static ResoniteFloat3 Cross3(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));
    }

    private static ResoniteFloat3 Scale3(ResoniteFloat3 value, double scalar)
    {
        return new ResoniteFloat3(value.X * scalar, value.Y * scalar, value.Z * scalar);
    }

    private static bool HasRenderableGeometry(ResoniteConstructionCityObject cityObject)
    {
        return cityObject.Geometry switch
        {
            ResoniteTriangleMeshGeometry triangleMesh => triangleMesh.Mesh.Submeshes.Count > 0,
            ResoniteHeightMapGridGeometry heightMap => heightMap.Width > 1 && heightMap.Height > 1,
            _ => false,
        };
    }

    private static HeightMapTriangle[] CreateDemHeightMapTriangles(
        ParsedCityObject cityObject,
        ResoniteFloat3 slotPosition,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian)
    {
        List<HeightMapTriangle> triangles = [];
        foreach (ParsedSurface surface in cityObject.Surfaces)
        {
            ResoniteFloat3[] positions = surface.ExteriorRing.Vertices
                .Select(point => CreateGlobalHeightMapLocalPosition(point, slotPosition, globalOriginPoint, globalCartesian))
                .ToArray();
            if (positions.Length < 3)
            {
                continue;
            }

            ResoniteFloat3 origin = positions[0];
            for (int index = 1; index + 1 < positions.Length; index++)
            {
                triangles.Add(new HeightMapTriangle(origin, positions[index], positions[index + 1]));
            }
        }

        return triangles.ToArray();
    }

    private static ResoniteFloat3 CreateGlobalHeightMapLocalPosition(
        GeodeticPoint point,
        ResoniteFloat3 slotPosition,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian)
    {
        ResoniteFloat3 globalPosition = CreateResonitePosition(point, globalOriginPoint, globalCartesian);
        return new ResoniteFloat3(
            globalPosition.X - slotPosition.X,
            globalPosition.Y - slotPosition.Y,
            globalPosition.Z - slotPosition.Z);
    }

    private static bool TrySampleLocalDemHeight(
        double x,
        double z,
        IReadOnlyList<HeightMapTriangle> triangles,
        HeightMapSpatialIndex spatialIndex,
        out double height)
    {
        foreach (int triangleIndex in spatialIndex.GetCandidateTriangleIndices(x, z))
        {
            HeightMapTriangle triangle = triangles[triangleIndex];
            if (TryInterpolateLocalTriangleHeight(triangle, x, z, out height))
            {
                return true;
            }
        }

        height = 0.0;
        return false;
    }

    private static bool TryInterpolateLocalTriangleHeight(
        HeightMapTriangle triangle,
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

    private static void ValidateCompatibleReferenceSystem(
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

    private static IEnumerable<(ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)> SplitParsedCityObject(
        ParsedCityObject parsedCityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas = null)
    {
        foreach ((ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) entry in DemTerrainOverlayAssignment.SplitParsedCityObject(
                     parsedCityObject,
                     demTerrainTextureOverlays,
                     requestedMeshAreas))
        {
            yield return entry;
        }
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
        string SourceUnitIdentity,
        string SourceIdentity,
        bool SharedAcrossMeshCodes,
        bool TerrainAligned = false,
        GeodeticPoint? OriginOverride = null,
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
        IReadOnlyList<ResoniteFloat2>? UVs);

    private sealed record HeightMapTriangle(
        ResoniteFloat3 A,
        ResoniteFloat3 B,
        ResoniteFloat3 C);

    private sealed record DemHeightMapBounds(
        double MinX,
        double MaxX,
        double MinZ,
        double MaxZ);

    private sealed class HeightMapSpatialIndex
    {
        private readonly int[] allTriangleIndices;
        private readonly List<int>[] triangleBuckets;
        private readonly double minX;
        private readonly double minZ;
        private readonly double inverseCellSizeX;
        private readonly double inverseCellSizeZ;
        private readonly int cellsX;
        private readonly int cellsZ;

        private HeightMapSpatialIndex(
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

        public static HeightMapSpatialIndex Create(
            IReadOnlyList<HeightMapTriangle> triangles,
            double minX,
            double maxX,
            double minZ,
            double maxZ)
        {
            int[] allTriangleIndices = Enumerable.Range(0, triangles.Count).ToArray();
            if (triangles.Count == 0)
            {
                return new HeightMapSpatialIndex(allTriangleIndices, [], minX, minZ, 1.0, 1.0, 1, 1);
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
                HeightMapTriangle triangle = triangles[triangleIndex];
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

            return new HeightMapSpatialIndex(
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
        ResoniteFloat3[] Side0Positions,
        ResoniteFloat3[] Side1Positions,
        ResoniteFloat2[]? Side0Uvs,
        ResoniteFloat2[]? Side1Uvs,
        double Length,
        double Width,
        double Side0EdgeLength,
        double Side1EdgeLength);

    private readonly record struct SurfaceSliceSample(
        GeodeticPoint Point,
        ResoniteFloat2? UV,
        double LateralPosition);

    internal sealed record ParsedSurface(
        string PolygonId,
        ParsedSurfaceSemantic Semantic,
        ParsedRing ExteriorRing,
        ParsedRing[] InteriorRings,
        ResoniteColor BaseColor,
        ResoniteTexturePayload? TexturePayload,
        bool UsesGeneratedDemTexture = false)
    {
        public IEnumerable<GeodeticPoint> Vertices =>
            ExteriorRing.Vertices.Concat(InteriorRings.SelectMany(static ring => ring.Vertices));
    }

    internal sealed record ResolvedSurfaceMaterial(
        ParsedSurface Surface,
        ResolvedMaterial Material,
        ResoniteMaterialDepthOffset? DepthOffset);

    private sealed record TessellatedVertex(
        ResoniteFloat3 Position,
        ResoniteFloat2 UV,
        ResoniteColor? Color);

    private sealed record TessellatedRing(
        string RingId,
        IReadOnlyList<TessellatedVertex> Vertices);

    private sealed record TessVertexPayload(
        ResoniteFloat3 Position,
        ResoniteFloat2 UV,
        ResoniteColor? Color);

    private sealed record SurfaceUvAxes(
        ResoniteFloat3 AxisU,
        ResoniteFloat3 AxisV);

    private sealed record SurfaceUvProjection(
        ResoniteFloat3 AxisU,
        ResoniteFloat3 AxisV);

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

            Dictionary<TerrainGridCell, TerrainHeightPoint[]> pointsByCell = BuildPointIndex(points, minX, minZ, cellSize);
            Dictionary<TerrainGridCell, ProjectedTerrainHeightTriangle[]> trianglesByCell =
                BuildTriangleIndex(triangles, minX, minZ, cellSize);

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

        private static Dictionary<TerrainGridCell, TerrainHeightPoint[]> BuildPointIndex(
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

        private static Dictionary<TerrainGridCell, ProjectedTerrainHeightTriangle[]> BuildTriangleIndex(
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

    private sealed record SurfaceAppearance(
        ResoniteColor BaseColor,
        ResoniteTexturePayload? TexturePayload,
        IReadOnlyDictionary<string, IReadOnlyList<ResoniteFloat2>>? RingUvsByRingId);

    private sealed record TextureAssignment(
        string ResolvedTexturePath,
        IReadOnlyDictionary<string, IReadOnlyList<ResoniteFloat2>> RingCoordinates);

    private sealed class AppearanceLibrary
    {
        private readonly Dictionary<string, ResoniteColor> colorsByPolygonId;
        private readonly IPlateauDatasetContentSource datasetSource;
        private readonly Dictionary<string, ResoniteTexturePayload> texturePayloadsByResolvedPath;
        private readonly Dictionary<string, TextureAssignment> texturesByPolygonId;

        internal AppearanceLibrary(
            Dictionary<string, ResoniteColor> colorsByPolygonId,
            IPlateauDatasetContentSource datasetSource,
            Dictionary<string, TextureAssignment> texturesByPolygonId)
        {
            this.colorsByPolygonId = colorsByPolygonId;
            this.datasetSource = datasetSource;
            this.texturesByPolygonId = texturesByPolygonId;
            texturePayloadsByResolvedPath = new Dictionary<string, ResoniteTexturePayload>(StringComparer.Ordinal);
        }

        public static AppearanceLibrary Parse(
            XDocument document,
            string sourceFileRelativePath,
            IPlateauDatasetContentSource datasetSource)
        {
            Dictionary<string, ResoniteColor> colorsByPolygonId = new(StringComparer.Ordinal);
            Dictionary<string, TextureAssignment> texturesByPolygonId = new(StringComparer.Ordinal);

            foreach (XElement textureElement in document.Descendants(App + "ParameterizedTexture"))
            {
                string? imageUri = textureElement.Element(App + "imageURI")?.Value.Trim();
                string? resolvedTexturePath = ResolveTexturePath(sourceFileRelativePath, datasetSource, imageUri);
                if (resolvedTexturePath is null)
                {
                    continue;
                }

                foreach (XElement targetElement in textureElement.Elements(App + "target"))
                {
                    string? polygonId = StripReferencePrefix(targetElement.Attribute("uri")?.Value);
                    if (string.IsNullOrWhiteSpace(polygonId))
                    {
                        continue;
                    }

                    Dictionary<string, IReadOnlyList<ResoniteFloat2>> ringCoordinates = new(StringComparer.Ordinal);
                    foreach (XElement textureCoordinatesElement in targetElement.Descendants(App + "textureCoordinates"))
                    {
                        string? ringId = StripReferencePrefix(textureCoordinatesElement.Attribute("ring")?.Value);
                        if (string.IsNullOrWhiteSpace(ringId))
                        {
                            continue;
                        }

                        List<ResoniteFloat2> coordinates = ParseTextureCoordinates(textureCoordinatesElement.Value);
                        if (coordinates.Count > 0)
                        {
                            ringCoordinates[ringId] = coordinates;
                        }
                    }

                    texturesByPolygonId[polygonId] = new TextureAssignment(resolvedTexturePath, ringCoordinates);
                }
            }

            foreach (XElement materialElement in document.Descendants(App + "X3DMaterial"))
            {
                ResoniteColor diffuseColor = ParseColor(
                    materialElement.Element(App + "diffuseColor")?.Value,
                    DefaultMaterialColor);

                foreach (XElement targetElement in materialElement.Elements(App + "target"))
                {
                    string? polygonId = StripReferencePrefix(targetElement.Attribute("uri")?.Value);
                    if (!string.IsNullOrWhiteSpace(polygonId))
                    {
                        colorsByPolygonId[polygonId] = diffuseColor;
                    }
                }
            }

            return new AppearanceLibrary(colorsByPolygonId, datasetSource, texturesByPolygonId);
        }

        public static async Task<AppearanceLibrary> ParseAsync(
            string sourceFileRelativePath,
            IPlateauDatasetContentSource datasetSource,
            CancellationToken cancellationToken)
        {
            Dictionary<string, ResoniteColor> colorsByPolygonId = new(StringComparer.Ordinal);
            Dictionary<string, TextureAssignment> texturesByPolygonId = new(StringComparer.Ordinal);

            await using Stream stream = await datasetSource.OpenReadAsync(sourceFileRelativePath, cancellationToken);
            System.Xml.XmlReaderSettings settings = new()
            {
                Async = true,
                IgnoreComments = true,
                IgnoreWhitespace = true,
                DtdProcessing = System.Xml.DtdProcessing.Ignore,
            };
            using System.Xml.XmlReader reader = System.Xml.XmlReader.Create(stream, settings);

            while (await reader.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType != System.Xml.XmlNodeType.Element
                    || !string.Equals(reader.NamespaceURI, App.NamespaceName, StringComparison.Ordinal))
                {
                    continue;
                }

                switch (reader.LocalName)
                {
                    case "ParameterizedTexture":
                        using (System.Xml.XmlReader subtreeReader = reader.ReadSubtree())
                        {
                            XElement textureElement = await XElement.LoadAsync(subtreeReader, LoadOptions.None, cancellationToken);
                            ParseParameterizedTexture(textureElement, sourceFileRelativePath, datasetSource, texturesByPolygonId);
                        }

                        break;
                    case "X3DMaterial":
                        using (System.Xml.XmlReader subtreeReader = reader.ReadSubtree())
                        {
                            XElement materialElement = await XElement.LoadAsync(subtreeReader, LoadOptions.None, cancellationToken);
                            ParseX3DMaterial(materialElement, colorsByPolygonId);
                        }

                        break;
                }
            }

            return new AppearanceLibrary(colorsByPolygonId, datasetSource, texturesByPolygonId);
        }

        public SurfaceAppearance Resolve(string polygonId)
        {
            ResoniteColor baseColor = colorsByPolygonId.TryGetValue(polygonId, out ResoniteColor? color)
                ? color
                : DefaultMaterialColor;

            if (!texturesByPolygonId.TryGetValue(polygonId, out TextureAssignment? textureAssignment))
            {
                return new SurfaceAppearance(baseColor, null, null);
            }

            if (!texturePayloadsByResolvedPath.TryGetValue(textureAssignment.ResolvedTexturePath, out ResoniteTexturePayload? texturePayload))
            {
                texturePayload = LoadTexturePayload(datasetSource, textureAssignment.ResolvedTexturePath);
                if (texturePayload is not null)
                {
                    texturePayloadsByResolvedPath[textureAssignment.ResolvedTexturePath] = texturePayload;
                }
            }

            return new SurfaceAppearance(baseColor, texturePayload, textureAssignment.RingCoordinates);
        }

        private static string? ResolveTexturePath(
            string sourceFileRelativePath,
            IPlateauDatasetContentSource datasetSource,
            string? imageUri)
        {
            if (string.IsNullOrWhiteSpace(imageUri))
            {
                return null;
            }

            string? resolvedTexturePath = datasetSource.ResolveRelativePath(
                sourceFileRelativePath,
                imageUri);
            if (resolvedTexturePath is null || !datasetSource.FileExists(resolvedTexturePath))
            {
                return null;
            }

            return resolvedTexturePath;
        }

        private static ResoniteTexturePayload? LoadTexturePayload(
            IPlateauDatasetContentSource datasetSource,
            string resolvedTexturePath)
        {
            using Stream stream = datasetSource.OpenReadAsync(resolvedTexturePath, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            using MemoryStream encodedPayloadStream = new();
            stream.CopyTo(encodedPayloadStream);
            return new ResoniteTexturePayload(
                Width: null,
                Height: null,
                "sRGB",
                encodedPayloadStream.ToArray(),
                $"dataset:{resolvedTexturePath}",
                ResoniteTexturePayloadFormat.EncodedImage);
        }

        private static string? StripReferencePrefix(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.StartsWith('#')
                ? value[1..]
                : value;
        }

        private static ResoniteColor ParseColor(string? value, ResoniteColor fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            double[] values = ParseDoubles(value);
            if (values.Length < 3)
            {
                return fallback;
            }

            return new ResoniteColor(
                R: values[0],
                G: values[1],
                B: values[2],
                A: values.Length >= 4 ? values[3] : 1.0);
        }

        internal static void ParseParameterizedTexture(
            XElement textureElement,
            string sourceFileRelativePath,
            IPlateauDatasetContentSource datasetSource,
            Dictionary<string, TextureAssignment> texturesByPolygonId)
        {
            string? imageUri = textureElement.Element(App + "imageURI")?.Value.Trim();
            string? resolvedTexturePath = ResolveTexturePath(sourceFileRelativePath, datasetSource, imageUri);
            if (resolvedTexturePath is null)
            {
                return;
            }

            foreach (XElement targetElement in textureElement.Elements(App + "target"))
            {
                string? polygonId = StripReferencePrefix(targetElement.Attribute("uri")?.Value);
                if (string.IsNullOrWhiteSpace(polygonId))
                {
                    continue;
                }

                Dictionary<string, IReadOnlyList<ResoniteFloat2>> ringCoordinates = new(StringComparer.Ordinal);
                foreach (XElement textureCoordinatesElement in targetElement.Descendants(App + "textureCoordinates"))
                {
                    string? ringId = StripReferencePrefix(textureCoordinatesElement.Attribute("ring")?.Value);
                    if (string.IsNullOrWhiteSpace(ringId))
                    {
                        continue;
                    }

                    List<ResoniteFloat2> coordinates = ParseTextureCoordinates(textureCoordinatesElement.Value);
                    if (coordinates.Count > 0)
                    {
                        ringCoordinates[ringId] = coordinates;
                    }
                }

                texturesByPolygonId[polygonId] = new TextureAssignment(resolvedTexturePath, ringCoordinates);
            }
        }

        internal static void ParseX3DMaterial(
            XElement materialElement,
            Dictionary<string, ResoniteColor> colorsByPolygonId)
        {
            ResoniteColor diffuseColor = ParseColor(
                materialElement.Element(App + "diffuseColor")?.Value,
                DefaultMaterialColor);

            foreach (XElement targetElement in materialElement.Elements(App + "target"))
            {
                string? polygonId = StripReferencePrefix(targetElement.Attribute("uri")?.Value);
                if (!string.IsNullOrWhiteSpace(polygonId))
                {
                    colorsByPolygonId[polygonId] = diffuseColor;
                }
            }
        }
    }
}
