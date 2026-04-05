using System.Globalization;
using System.Xml.Linq;

using GeographicLib;

using LibTessDotNet;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public static class LocalCityGmlResonitePlanBuilder
{
    public const string DefaultDemTerrainTexturePath = "terrain://dem/gsi-seamlessphoto";
    public const string DefaultDemTerrainTextureUrlTemplate = "https://cyberjapandata.gsi.go.jp/xyz/seamlessphoto/{z}/{x}/{y}.jpg";
    public const int DefaultDemTerrainTextureZoomLevel = 18;
    public const int DefaultDemTerrainTextureMaxSize = 4096;
    private static readonly ResoniteColor DefaultMaterialColor = new(1.0, 1.0, 1.0, 1.0);
    private static readonly XNamespace App = "http://www.opengis.net/citygml/appearance/2.0";
    private static readonly XNamespace Core = "http://www.opengis.net/citygml/2.0";
    private static readonly XNamespace Gml = "http://www.opengis.net/gml";

    public static ResoniteConstructionPlan BuildPlan(PlateauImportRequest request)
    {
        IResoniteConstructionSource source = CreateConstructionSource(request);
        ResoniteConstructionCityObject[] cityObjects = source.ReadCityObjects().ToArray();
        if (cityObjects.Length == 0)
        {
            throw new PlateauImportValidationException(
                [$"No triangulated CityGML geometry was produced for mesh code '{request.MeshCode}'."]);
        }

        return source.Metadata.ToPlan(cityObjects);
    }

    public static IResoniteConstructionSource CreateConstructionSource(PlateauImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SourceKind != DatasetSourceKind.Local || string.IsNullOrWhiteSpace(request.InputPath))
        {
            throw new PlateauImportValidationException(
                ["Local CityGML import requires --source local and a dataset root via --input."]);
        }

        string datasetRoot = Path.GetFullPath(request.InputPath);
        MeshCodeArea? requestedMeshArea = MeshCodeArea.TryParse(request.MeshCode);
        string[] sourceFileSearchCodes = GetSourceFileSearchCodes(request.MeshCode);
        SourceFileDescriptor[] sourceFiles = Directory
            .EnumerateFiles(datasetRoot, "*.gml", SearchOption.AllDirectories)
            .Select(path => CreateSourceFileDescriptor(datasetRoot, path, sourceFileSearchCodes))
            .Where(static descriptor => descriptor is not null)
            .Select(static descriptor => descriptor!)
            .OrderBy(static descriptor => GetPackageSendPriority(descriptor.PackageName))
            .ThenBy(static descriptor => descriptor.RelativePath, StringComparer.Ordinal)
            .ToArray();

        if (sourceFiles.Length == 0)
        {
            throw new PlateauImportValidationException(
                [$"No local PLATEAU CityGML files were found for mesh code '{request.MeshCode}' under udx/<package>/<mesh-code>/."]);
        }

        List<string> relativeSourceFiles = [];
        CoordinateReferenceSystem? referenceSystem = null;
        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude)? globalBounds = null;
        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude)? demBounds = null;
        bool foundGeometry = false;

        foreach (SourceFileDescriptor sourceFile in sourceFiles)
        {
            XDocument document = XDocument.Load(sourceFile.AbsolutePath, LoadOptions.None);
            string relativeSourceFile = sourceFile.RelativePath;
            relativeSourceFiles.Add(relativeSourceFile);

            ParsedCityObject[] cityObjectsFromFile = ParseCityObjects(
                document,
                sourceFile,
                datasetRoot,
                requestedMeshArea);

            if (cityObjectsFromFile.Length == 0)
            {
                continue;
            }

            foundGeometry = true;
            CoordinateReferenceSystem fileReferenceSystem = GetReferenceSystem(cityObjectsFromFile);
            if (referenceSystem is null)
            {
                referenceSystem = fileReferenceSystem;
            }
            else if (!referenceSystem.IsCompatibleWith(fileReferenceSystem))
            {
                throw new PlateauImportValidationException(
                    [$"Mixed CityGML coordinate reference systems are not supported. Found '{referenceSystem.SrsName}' and '{fileReferenceSystem.SrsName}'."]);
            }

            globalBounds = MergeBounds(globalBounds, GetBounds(cityObjectsFromFile));
            if (string.Equals(sourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
            {
                demBounds = MergeBounds(demBounds, GetBounds(cityObjectsFromFile));
            }
        }

        if (!foundGeometry || globalBounds is null || referenceSystem is null)
        {
            throw new PlateauImportValidationException(
                [$"No CityGML city object geometry was found for mesh code '{request.MeshCode}'."]);
        }

        TerrainTextureOverlay[] terrainTextureOverlays = referenceSystem.IsGeographic && demBounds is not null
            ? CreateDemTerrainTextureOverlays(demBounds.Value)
            : [];

        GeodeticPoint globalOriginPoint = CreateGlobalOrigin(globalBounds.Value);
        ResoniteAttribution attribution = PlateauResoniteAttributionFactory.Create(request);
        ResoniteConstructionMetadata metadata = new(
            SchemaVersion: "3.0",
            WorldName: $"PLATEAU {request.Dataset} {request.MeshCode}",
            Request: request,
            SourceDataset: new PlateauSourceDataset(
                PackageNames: sourceFiles
                    .Select(static sourceFile => sourceFile.PackageName)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static packageName => packageName, StringComparer.Ordinal)
                    .ToArray(),
                SourceFiles: relativeSourceFiles.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
                TerrainTextureOverlays: terrainTextureOverlays),
            Attribution: attribution,
            LocalOrigin: new ResoniteLocalOrigin(
                Latitude: globalOriginPoint.Latitude,
                Longitude: globalOriginPoint.Longitude,
                Altitude: globalOriginPoint.Altitude));

        return new ConstructionSource(
            metadata,
            datasetRoot,
            sourceFiles,
            requestedMeshArea,
            referenceSystem,
            globalOriginPoint);
    }

    private static string[] GetSourceFileSearchCodes(string meshCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(meshCode);

        if (meshCode.Length >= 8)
        {
            return [meshCode, meshCode[..6]];
        }

        return [meshCode];
    }

    private static SourceFileDescriptor? CreateSourceFileDescriptor(
        string datasetRoot,
        string path,
        string[] sourceFileSearchCodes)
    {
        string relativePath = NormalizePath(Path.GetRelativePath(datasetRoot, path));
        string[] segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3
            || !string.Equals(segments[0], "udx", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string packageName = segments[1];
        if (string.IsNullOrWhiteSpace(packageName))
        {
            return null;
        }

        string fileName = Path.GetFileName(path);
        string? matchedMeshCode = sourceFileSearchCodes
            .OrderByDescending(static code => code.Length)
            .FirstOrDefault(code => fileName.Contains(code, StringComparison.OrdinalIgnoreCase));
        if (matchedMeshCode is null)
        {
            return null;
        }

        return new SourceFileDescriptor(
            path,
            relativePath,
            packageName,
            matchedMeshCode.Length < sourceFileSearchCodes[0].Length);
    }

    private static ParsedCityObject? ParseCityObject(
        XElement cityObjectElement,
        string packageName,
        string relativeSourceFile,
        AppearanceLibrary appearanceLibrary,
        CoordinateReferenceSystem coordinateReferenceSystem,
        MeshCodeArea? requestedMeshArea)
    {
        string objectTypeName = cityObjectElement.Name.LocalName;
        string objectId = GetAttribute(cityObjectElement, Gml + "id") ?? objectTypeName;
        string? displayName = cityObjectElement.Elements(Gml + "name").FirstOrDefault()?.Value.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = objectId;
        }

        ParsedSurface[] surfaces = SelectPreferredLodSurfaceElements(cityObjectElement)
            .Select(surfaceElement => ParseSurface(surfaceElement, appearanceLibrary))
            .Where(static surface => surface is not null)
            .Select(static surface => surface!)
            .Select(surface => ApplyPackageSurfaceDefaults(packageName, surface))
            .OrderBy(static surface => surface.PolygonId, StringComparer.Ordinal)
            .ToArray();

        if (surfaces.Length == 0)
        {
            return null;
        }

        if (requestedMeshArea is not null
            && coordinateReferenceSystem.IsGeographic
            && !IntersectsMeshCodeArea(surfaces, requestedMeshArea))
        {
            return null;
        }

        string fileStem = Path.GetFileNameWithoutExtension(relativeSourceFile);
        string slotKey = SanitizeIdentifier($"{packageName}_{fileStem}_{objectId}");
        return new ParsedCityObject(slotKey, displayName!, packageName, surfaces, coordinateReferenceSystem);
    }

    private static TerrainTextureOverlay[] CreateDemTerrainTextureOverlays(
        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude) demBounds)
    {
        double leftPixel = WebMercatorTileMath.LongitudeToPixelX(demBounds.minLongitude, DefaultDemTerrainTextureZoomLevel);
        double rightPixel = WebMercatorTileMath.LongitudeToPixelX(demBounds.maxLongitude, DefaultDemTerrainTextureZoomLevel);
        double topPixel = WebMercatorTileMath.LatitudeToPixelY(demBounds.maxLatitude, DefaultDemTerrainTextureZoomLevel);
        double bottomPixel = WebMercatorTileMath.LatitudeToPixelY(demBounds.minLatitude, DefaultDemTerrainTextureZoomLevel);

        List<TerrainTextureOverlay> overlays = [];
        int row = 0;
        for (double currentTop = topPixel; currentTop < bottomPixel - 1e-6; currentTop += DefaultDemTerrainTextureMaxSize, row++)
        {
            double currentBottom = Math.Min(currentTop + DefaultDemTerrainTextureMaxSize, bottomPixel);
            int column = 0;
            for (double currentLeft = leftPixel; currentLeft < rightPixel - 1e-6; currentLeft += DefaultDemTerrainTextureMaxSize, column++)
            {
                double currentRight = Math.Min(currentLeft + DefaultDemTerrainTextureMaxSize, rightPixel);
                overlays.Add(CreateDemTerrainTextureOverlay(row, column, currentLeft, currentRight, currentTop, currentBottom));
            }
        }

        if (overlays.Count == 0)
        {
            overlays.Add(CreateDemTerrainTextureOverlay(row: 0, column: 0, leftPixel, rightPixel, topPixel, bottomPixel));
        }

        return overlays.Count == 1
            ?
            [
                overlays[0] with { TexturePath = DefaultDemTerrainTexturePath },
            ]
            : overlays.ToArray();
    }

    private static TerrainTextureOverlay CreateDemTerrainTextureOverlay(
        int row,
        int column,
        double leftPixel,
        double rightPixel,
        double topPixel,
        double bottomPixel)
    {
        return new TerrainTextureOverlay(
            TexturePath: $"{DefaultDemTerrainTexturePath}/{row:D2}-{column:D2}",
            PackageName: "dem",
            UrlTemplate: DefaultDemTerrainTextureUrlTemplate,
            ZoomLevel: DefaultDemTerrainTextureZoomLevel,
            GeographicBounds: new GeographicRectangle(
                MinLatitude: WebMercatorTileMath.PixelYToLatitude(bottomPixel, DefaultDemTerrainTextureZoomLevel),
                MaxLatitude: WebMercatorTileMath.PixelYToLatitude(topPixel, DefaultDemTerrainTextureZoomLevel),
                MinLongitude: WebMercatorTileMath.PixelXToLongitude(leftPixel, DefaultDemTerrainTextureZoomLevel),
                MaxLongitude: WebMercatorTileMath.PixelXToLongitude(rightPixel, DefaultDemTerrainTextureZoomLevel)),
            MaxTextureSize: DefaultDemTerrainTextureMaxSize);
    }

    private static XElement[] SelectPreferredLodSurfaceElements(XElement cityObjectElement)
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
        int? highestLod = explicitLodLevels.Length > 0
            ? explicitLodLevels.Max()
            : null;

        return highestLod.HasValue
            ? surfaces
                .Where(surface => surface.LodLevel == highestLod.Value)
                .Select(static surface => surface.SurfaceElement)
                .ToArray()
            : surfaces
                .Select(static surface => surface.SurfaceElement)
                .ToArray();
    }

    private static ParsedSurface ApplyPackageSurfaceDefaults(string packageName, ParsedSurface surface)
    {
        return string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(surface.TexturePath)
            ? surface with { TexturePath = DefaultDemTerrainTexturePath }
            : surface;
    }

    private static int GetPackageSendPriority(string packageName)
    {
        return string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase)
            ? 0
            : 1;
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
        string datasetRoot,
        MeshCodeArea? requestedMeshArea)
    {
        string relativeSourceFile = sourceFile.RelativePath;
        CoordinateReferenceSystem coordinateReferenceSystem = CoordinateReferenceSystem.Parse(document);
        AppearanceLibrary appearanceLibrary = AppearanceLibrary.Parse(
            document,
            Path.GetDirectoryName(sourceFile.AbsolutePath) ?? datasetRoot,
            datasetRoot);

        return document
            .Descendants(Core + "cityObjectMember")
            .Elements()
            .Select(cityObject => ParseCityObject(
                cityObject,
                sourceFile.PackageName,
                relativeSourceFile,
                appearanceLibrary,
                coordinateReferenceSystem,
                sourceFile.RequiresMeshAreaFilter ? requestedMeshArea : null))
            .Where(static cityObject => cityObject is not null)
            .Select(static cityObject => cityObject!)
            .OrderBy(static cityObject => cityObject.SlotKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IntersectsMeshCodeArea(
        IEnumerable<ParsedSurface> surfaces,
        MeshCodeArea meshCodeArea)
    {
        List<GeodeticPoint> vertices = surfaces
            .SelectMany(static surface => surface.Vertices)
            .ToList();

        double minLatitude = vertices.Min(static point => point.Latitude);
        double maxLatitude = vertices.Max(static point => point.Latitude);
        double minLongitude = vertices.Min(static point => point.Longitude);
        double maxLongitude = vertices.Max(static point => point.Longitude);

        return maxLatitude >= meshCodeArea.SouthLatitude
            && minLatitude <= meshCodeArea.NorthLatitude
            && maxLongitude >= meshCodeArea.WestLongitude
            && minLongitude <= meshCodeArea.EastLongitude;
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

    private static (
        double minLatitude,
        double maxLatitude,
        double minLongitude,
        double maxLongitude,
        double minAltitude) MergeBounds(
        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude)? current,
        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude) next)
    {
        if (current is null)
        {
            return next;
        }

        return (
            Math.Min(current.Value.minLatitude, next.minLatitude),
            Math.Max(current.Value.maxLatitude, next.maxLatitude),
            Math.Min(current.Value.minLongitude, next.minLongitude),
            Math.Max(current.Value.maxLongitude, next.maxLongitude),
            Math.Min(current.Value.minAltitude, next.minAltitude));
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

        string polygonId = GetAttribute(polygonElement, Gml + "id") ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
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
            ExteriorRing: exteriorParsedRing,
            InteriorRings: interiorRings,
            BaseColor: appearance.BaseColor,
            TexturePath: appearance.TexturePath);
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
            ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        IReadOnlyList<GeodeticPoint> vertices = ParseRingPoints(ringElement);
        if (vertices.Count < 3)
        {
            return null;
        }

        IReadOnlyList<ResoniteFloat2>? uvs = null;
        if (ringUvsByRingId is not null
            && ringUvsByRingId.TryGetValue(ringId, out IReadOnlyList<ResoniteFloat2>? ringUvs)
            && ringUvs.Count == vertices.Count)
        {
            uvs = ringUvs;
        }

        return new ParsedRing(ringId, vertices, uvs);
    }

    private static List<GeodeticPoint> ParseRingPoints(XElement ringElement)
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

        return points;
    }

    private static GeodeticPoint ComputeGlobalOrigin(IEnumerable<ParsedCityObject> cityObjects)
    {
        return CreateGlobalOrigin(GetBounds(cityObjects));
    }

    private static GeodeticPoint CreateGlobalOrigin(
        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude) bounds)
    {
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

    private static ResoniteConstructionCityObject MaterializeCityObject(
        ParsedCityObject cityObject,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay)
    {
        List<GeodeticPoint> allPoints = cityObject.Surfaces.SelectMany(static surface => surface.Vertices).ToList();
        double minLatitude = allPoints.Min(static point => point.Latitude);
        double maxLatitude = allPoints.Max(static point => point.Latitude);
        double minLongitude = allPoints.Min(static point => point.Longitude);
        double maxLongitude = allPoints.Max(static point => point.Longitude);
        double minAltitude = allPoints.Min(static point => point.Altitude);

        GeodeticPoint cityObjectOrigin = new(
            Latitude: (minLatitude + maxLatitude) / 2.0,
            Longitude: (minLongitude + maxLongitude) / 2.0,
            Altitude: minAltitude);

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

        MaterializedSurface[] materializedSurfaces = cityObject.Surfaces
            .Select(surface => MaterializeSurfaceMaterial(cityObject, cityObjectOrigin, cityObjectCartesian, surface))
            .ToArray();

        IReadOnlyList<IGrouping<string, MaterializedSurface>> materialGroups = materializedSurfaces
            .GroupBy(
                static materializedSurface => CreateMaterialKey(
                    materializedSurface.Material.TexturePath,
                    materializedSurface.Material.TextureSourceKind,
                    materializedSurface.Material.Projection,
                    materializedSurface.Surface.BaseColor),
                StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToArray();

        for (int materialIndex = 0; materialIndex < materialGroups.Count; materialIndex++)
        {
            IGrouping<string, MaterializedSurface> materialGroup = materialGroups[materialIndex];
            List<int> indices = [];

            foreach (ParsedSurface surface in materialGroup
                         .Select(static materializedSurface => materializedSurface.Surface)
                         .OrderBy(static surface => surface.PolygonId, StringComparer.Ordinal))
            {
                TriangulateSurface(
                    surface,
                    cityObject.PackageName,
                    cityObjectOrigin,
                    cityObjectCartesian,
                    globalOriginPoint,
                    globalCartesian,
                    demTerrainTextureOverlay,
                    vertices,
                    indices);
            }

            if (indices.Count == 0)
            {
                continue;
            }

            MaterializedSurface representativeSurface = materialGroup.First();
            submeshes.Add(new ResoniteMeshSubmesh(materialIndex, materialGroup.Key, indices));
            materials.Add(
                new ResoniteMaterialBinding(
                    MaterialKey: materialGroup.Key,
                    BaseColor: representativeSurface.Surface.BaseColor,
                    TexturePath: representativeSurface.Material.TexturePath,
                    TextureSourceKind: representativeSurface.Material.TextureSourceKind,
                    Projection: representativeSurface.Material.Projection,
                    SubmeshIndices: [materialIndex]));
        }

        return new ResoniteConstructionCityObject(
            SlotKey: cityObject.SlotKey,
            DisplayName: cityObject.DisplayName,
            PackageName: cityObject.PackageName,
            Transform: new ResoniteTransform(slotPosition),
            Mesh: new ResoniteImportedMesh(vertices, submeshes),
            Materials: materials);
    }

    private static MaterializedSurface MaterializeSurfaceMaterial(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        ParsedSurface surface)
    {
        if (IsGeneratedDemTexturePath(surface.TexturePath))
        {
            return new MaterializedSurface(
                surface,
                new DefaultMaterialCatalog.ResolvedMaterial(
                    surface.TexturePath,
                    ResoniteTextureSourceKind.Bundled,
                    ResoniteMaterialProjection.Uv,
                    Family: null));
        }

        bool preferUvProjection = ShouldPreferUvProjection(
            cityObject.PackageName,
            surface,
            cityObjectOrigin,
            cityObjectCartesian);
        DefaultMaterialCatalog.ResolvedMaterial resolvedMaterial = DefaultMaterialCatalog.ResolveMaterial(
            cityObject.PackageName,
            surface.TexturePath,
            preferUvProjection,
            $"{cityObject.SlotKey}:{surface.PolygonId}:{(preferUvProjection ? "uv" : "triplanar")}");
        return new MaterializedSurface(surface, resolvedMaterial);
    }

    private static void TriangulateSurface(
        ParsedSurface surface,
        string packageName,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        List<ResoniteMeshVertex> vertices,
        List<int> indices)
    {
        bool useGeneratedDemUv = string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase)
            && IsGeneratedDemTexturePath(surface.TexturePath)
            && demTerrainTextureOverlay is not null;
        List<TessellatedRing> tessellatedRings = CreateTessellatedRings(
            surface,
            cityObjectOrigin,
            cityObjectCartesian,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlay,
            useGeneratedDemUv);
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

            TessVertexData vertex0 = GetTessVertexData(tessellator, element0);
            TessVertexData vertex1 = GetTessVertexData(tessellator, element1);
            TessVertexData vertex2 = GetTessVertexData(tessellator, element2);

            ResoniteFloat3 position0 = vertex0.Position;
            ResoniteFloat3 position1 = vertex1.Position;
            ResoniteFloat3 position2 = vertex2.Position;
            ResoniteFloat2 uv0 = vertex0.UV;
            ResoniteFloat2 uv1 = vertex1.UV;
            ResoniteFloat2 uv2 = vertex2.UV;

            ResoniteFloat3? triangleNormal = ComputeNormal(position0, position1, position2);
            if (triangleNormal is null)
            {
                continue;
            }

            if (Dot(triangleNormal, expectedNormal) < 0.0)
            {
                (position1, position2) = (position2, position1);
                (uv1, uv2) = (uv2, uv1);
                triangleNormal = ComputeNormal(position0, position1, position2);
                if (triangleNormal is null)
                {
                    continue;
                }
            }

            int baseIndex = vertices.Count;
            vertices.Add(new ResoniteMeshVertex(position0, triangleNormal, uv0));
            vertices.Add(new ResoniteMeshVertex(position1, triangleNormal, uv1));
            vertices.Add(new ResoniteMeshVertex(position2, triangleNormal, uv2));

            indices.Add(baseIndex);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 1);
        }
    }

    private static List<TessellatedRing> CreateTessellatedRings(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        bool useGeneratedDemUv)
    {
        List<TessellatedRing> rings =
        [
            CreateTessellatedRing(
                surface.ExteriorRing,
                cityObjectOrigin,
                cityObjectCartesian,
                globalOriginPoint,
                globalCartesian,
                demTerrainTextureOverlay,
                useGeneratedDemUv),
        ];
        rings.AddRange(surface.InteriorRings.Select(ring => CreateTessellatedRing(
            ring,
            cityObjectOrigin,
            cityObjectCartesian,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlay,
            useGeneratedDemUv)));
        return rings.Where(static ring => ring.Vertices.Count >= 3).ToList();
    }

    private static TessellatedRing CreateTessellatedRing(
        ParsedRing ring,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        bool useGeneratedDemUv)
    {
        TessellatedVertex[] vertices = ring.Vertices
            .Select((point, index) => new TessellatedVertex(
                CreateResonitePosition(point, cityObjectOrigin, cityObjectCartesian),
                ring.UVs is not null && index < ring.UVs.Count
                    ? ring.UVs[index]
                    : useGeneratedDemUv
                        ? CreateGeneratedDemUv(point, demTerrainTextureOverlay!)
                        : new ResoniteFloat2(0.0, 0.0)))
            .ToArray();
        return new TessellatedRing(ring.RingId, vertices);
    }

    private static ResoniteFloat2 CreateGeneratedDemUv(
        GeodeticPoint point,
        TerrainTextureOverlay demTerrainTextureOverlay)
    {
        GeographicRectangle bounds = demTerrainTextureOverlay.GeographicBounds;
        double west = WebMercatorTileMath.LongitudeToNormalizedX(bounds.MinLongitude);
        double east = WebMercatorTileMath.LongitudeToNormalizedX(bounds.MaxLongitude);
        double north = WebMercatorTileMath.LatitudeToNormalizedY(bounds.MaxLatitude);
        double south = WebMercatorTileMath.LatitudeToNormalizedY(bounds.MinLatitude);
        double pointX = WebMercatorTileMath.LongitudeToNormalizedX(point.Longitude);
        double pointY = WebMercatorTileMath.LatitudeToNormalizedY(point.Latitude);
        double width = Math.Max(east - west, 1e-12);
        double height = Math.Max(south - north, 1e-12);

        return new ResoniteFloat2(
            Math.Clamp((pointX - west) / width, 0.0, 1.0),
            Math.Clamp((south - pointY) / height, 0.0, 1.0));
    }

    private static bool IsGeneratedDemTexturePath(string? texturePath)
    {
        return !string.IsNullOrWhiteSpace(texturePath)
            && texturePath.StartsWith(DefaultDemTerrainTexturePath, StringComparison.Ordinal);
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
            Data = new TessVertexData(vertex.Position, vertex.UV),
        };
    }

    private static object CombineTessVertexData(Vec3 position, object[] data, float[] weights)
    {
        double x = 0.0;
        double y = 0.0;
        double z = 0.0;
        double u = 0.0;
        double v = 0.0;

        for (int index = 0; index < data.Length; index++)
        {
            if (data[index] is not TessVertexData vertexData)
            {
                continue;
            }

            double weight = weights[index];
            x += vertexData.Position.X * weight;
            y += vertexData.Position.Y * weight;
            z += vertexData.Position.Z * weight;
            u += vertexData.UV.X * weight;
            v += vertexData.UV.Y * weight;
        }

        return new TessVertexData(
            new ResoniteFloat3(x, y, z),
            new ResoniteFloat2(u, v));
    }

    private static TessVertexData GetTessVertexData(Tess tessellator, int elementIndex)
    {
        return tessellator.Vertices[elementIndex].Data as TessVertexData
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
        string? texturePath,
        ResoniteTextureSourceKind textureSourceKind,
        ResoniteMaterialProjection projection,
        ResoniteColor color)
    {
        string colorKey = string.Create(
            CultureInfo.InvariantCulture,
            $"{color.R:0.######},{color.G:0.######},{color.B:0.######},{color.A:0.######}");

        return texturePath is null
            ? $"color:{colorKey}"
            : $"{projection.ToString().ToLowerInvariant()}|{textureSourceKind.ToString().ToLowerInvariant()}-texture:{texturePath.ToLowerInvariant()}|color:{colorKey}";
    }

    private static bool ShouldPreferUvProjection(
        string packageName,
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (surface.TexturePath is not null)
        {
            return true;
        }

        if (string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(packageName, "bldg", StringComparison.Ordinal)
            && !string.Equals(packageName, "ubld", StringComparison.Ordinal))
        {
            return false;
        }

        return IsFacadeSurface(surface, cityObjectOrigin, cityObjectCartesian);
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

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string? GetAttribute(XElement element, XName attributeName)
    {
        return element.Attribute(attributeName)?.Value;
    }

    private static double[] ParseDoubles(string value)
    {
        return value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => double.Parse(token, CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static List<ResoniteFloat2> ParseTextureCoordinates(string value)
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

    private static bool AreSameUV(ResoniteFloat2 left, ResoniteFloat2 right)
    {
        return Math.Abs(left.X - right.X) < 1e-8
            && Math.Abs(left.Y - right.Y) < 1e-8;
    }

    private static string SanitizeIdentifier(string value)
    {
        return string.Concat(
            value.Select(character => char.IsLetterOrDigit(character) ? character : '_'));
    }

    private sealed class ConstructionSource(
        ResoniteConstructionMetadata metadata,
        string datasetRoot,
        IReadOnlyList<SourceFileDescriptor> sourceFiles,
        MeshCodeArea? requestedMeshArea,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint)
        : IResoniteConstructionSource
    {
        private readonly TerrainTextureOverlay[] demTerrainTextureOverlays = metadata.SourceDataset.TerrainTextureOverlays
            .Where(static overlay => string.Equals(overlay.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static overlay => overlay.TexturePath, StringComparer.Ordinal)
            .ToArray();

        public ResoniteConstructionMetadata Metadata { get; } = metadata;

        public IEnumerable<ResoniteConstructionCityObject> ReadCityObjects()
        {
            LocalCartesian? globalCartesian = referenceSystem.IsGeographic
                ? new LocalCartesian(
                    globalOriginPoint.Latitude,
                    globalOriginPoint.Longitude,
                    globalOriginPoint.Altitude,
                    referenceSystem.Geocentric)
                : null;

            foreach (SourceFileDescriptor sourceFile in sourceFiles)
            {
                XDocument document = XDocument.Load(sourceFile.AbsolutePath, LoadOptions.None);
                foreach (ResoniteConstructionCityObject cityObject in MaterializeCityObjects(
                    document,
                    sourceFile,
                    datasetRoot,
                    requestedMeshArea,
                    globalOriginPoint,
                    globalCartesian,
                    demTerrainTextureOverlays))
                {
                    yield return cityObject;
                }
            }
        }

        public async IAsyncEnumerable<ResoniteConstructionCityObject> ReadCityObjectsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LocalCartesian? globalCartesian = referenceSystem.IsGeographic
                ? new LocalCartesian(
                    globalOriginPoint.Latitude,
                    globalOriginPoint.Longitude,
                    globalOriginPoint.Altitude,
                    referenceSystem.Geocentric)
                : null;

            foreach (SourceFileDescriptor sourceFile in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using FileStream stream = new(
                    sourceFile.AbsolutePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 16 * 1024,
                    useAsync: true);
                XDocument document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

                foreach (ResoniteConstructionCityObject cityObject in MaterializeCityObjects(
                    document,
                    sourceFile,
                    datasetRoot,
                    requestedMeshArea,
                    globalOriginPoint,
                    globalCartesian,
                    demTerrainTextureOverlays))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return cityObject;
                }
            }
        }
    }

    private static IEnumerable<ResoniteConstructionCityObject> MaterializeCityObjects(
        XDocument document,
        SourceFileDescriptor sourceFile,
        string datasetRoot,
        MeshCodeArea? requestedMeshArea,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays)
    {
        ParsedCityObject[] parsedCityObjects = ParseCityObjects(document, sourceFile, datasetRoot, requestedMeshArea);

        foreach (ParsedCityObject parsedCityObject in parsedCityObjects)
        {
            foreach ((ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) splitCityObject in SplitParsedCityObject(
                         parsedCityObject,
                         demTerrainTextureOverlays))
            {
                ResoniteConstructionCityObject cityObject = MaterializeCityObject(
                    splitCityObject.CityObject,
                    globalOriginPoint,
                    globalCartesian,
                    splitCityObject.Overlay);

                if (cityObject.Mesh.Submeshes.Count > 0)
                {
                    yield return cityObject;
                }
            }
        }
    }

    private static IEnumerable<(ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)> SplitParsedCityObject(
        ParsedCityObject parsedCityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays)
    {
        if (!string.Equals(parsedCityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            || demTerrainTextureOverlays.Count == 0)
        {
            yield return (parsedCityObject, null);
            yield break;
        }

        IReadOnlyList<IGrouping<string, ParsedSurface>> groups = parsedCityObject.Surfaces
            .Where(static surface => IsGeneratedDemTexturePath(surface.TexturePath))
            .Select(surface =>
            {
                TerrainTextureOverlay overlay = SelectDemTerrainTextureOverlay(surface, demTerrainTextureOverlays);
                return surface with { TexturePath = overlay.TexturePath };
            })
            .GroupBy(static surface => surface.TexturePath!, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToArray();

        if (groups.Count == 0)
        {
            yield return (parsedCityObject, null);
            yield break;
        }

        if (groups.Count == 1 && groups[0].Count() == parsedCityObject.Surfaces.Length)
        {
            yield return (
                parsedCityObject with
                {
                    Surfaces = groups[0].ToArray(),
                },
                FindOverlay(groups[0].Key, demTerrainTextureOverlays));
            yield break;
        }

        for (int index = 0; index < groups.Count; index++)
        {
            IGrouping<string, ParsedSurface> group = groups[index];
            yield return (
                parsedCityObject with
                {
                    SlotKey = $"{parsedCityObject.SlotKey}_dem_{index:D2}",
                    DisplayName = groups.Count > 1 ? $"{parsedCityObject.DisplayName} ({index + 1})" : parsedCityObject.DisplayName,
                    Surfaces = group.ToArray(),
                },
                FindOverlay(group.Key, demTerrainTextureOverlays));
        }
    }

    private static TerrainTextureOverlay FindOverlay(
        string texturePath,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays)
    {
        return demTerrainTextureOverlays.First(overlay => string.Equals(overlay.TexturePath, texturePath, StringComparison.Ordinal));
    }

    private static TerrainTextureOverlay SelectDemTerrainTextureOverlay(
        ParsedSurface surface,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays)
    {
        double minLatitude = surface.Vertices.Min(static point => point.Latitude);
        double maxLatitude = surface.Vertices.Max(static point => point.Latitude);
        double minLongitude = surface.Vertices.Min(static point => point.Longitude);
        double maxLongitude = surface.Vertices.Max(static point => point.Longitude);

        TerrainTextureOverlay? containingOverlay = demTerrainTextureOverlays.FirstOrDefault(overlay =>
            minLatitude >= overlay.GeographicBounds.MinLatitude
            && maxLatitude <= overlay.GeographicBounds.MaxLatitude
            && minLongitude >= overlay.GeographicBounds.MinLongitude
            && maxLongitude <= overlay.GeographicBounds.MaxLongitude);
        if (containingOverlay is not null)
        {
            return containingOverlay;
        }

        double centerLatitude = (minLatitude + maxLatitude) / 2.0;
        double centerLongitude = (minLongitude + maxLongitude) / 2.0;
        TerrainTextureOverlay? centerOverlay = demTerrainTextureOverlays.FirstOrDefault(overlay =>
            centerLatitude >= overlay.GeographicBounds.MinLatitude
            && centerLatitude <= overlay.GeographicBounds.MaxLatitude
            && centerLongitude >= overlay.GeographicBounds.MinLongitude
            && centerLongitude <= overlay.GeographicBounds.MaxLongitude);
        if (centerOverlay is not null)
        {
            return centerOverlay;
        }

        return demTerrainTextureOverlays.First(overlay =>
            maxLatitude >= overlay.GeographicBounds.MinLatitude
            && minLatitude <= overlay.GeographicBounds.MaxLatitude
            && maxLongitude >= overlay.GeographicBounds.MinLongitude
            && minLongitude <= overlay.GeographicBounds.MaxLongitude);
    }

    private sealed record ParsedCityObject(
        string SlotKey,
        string DisplayName,
        string PackageName,
        ParsedSurface[] Surfaces,
        CoordinateReferenceSystem ReferenceSystem);

    private sealed record SourceFileDescriptor(
        string AbsolutePath,
        string RelativePath,
        string PackageName,
        bool RequiresMeshAreaFilter);

    private sealed record MeshCodeArea(
        double SouthLatitude,
        double NorthLatitude,
        double WestLongitude,
        double EastLongitude)
    {
        public static MeshCodeArea? TryParse(string meshCode)
        {
            if (string.IsNullOrWhiteSpace(meshCode)
                || (meshCode.Length != 6 && meshCode.Length != 8)
                || !meshCode.All(char.IsDigit))
            {
                return null;
            }

            int firstLatitudeIndex = int.Parse(meshCode[..2], CultureInfo.InvariantCulture);
            int firstLongitudeIndex = int.Parse(meshCode[2..4], CultureInfo.InvariantCulture);

            double southLatitude = firstLatitudeIndex / 1.5;
            double westLongitude = 100.0 + firstLongitudeIndex;
            double latitudeSpan = 40.0 / 60.0;
            double longitudeSpan = 1.0;

            if (meshCode.Length >= 6)
            {
                int secondLatitudeIndex = int.Parse(meshCode[4].ToString(), CultureInfo.InvariantCulture);
                int secondLongitudeIndex = int.Parse(meshCode[5].ToString(), CultureInfo.InvariantCulture);
                latitudeSpan /= 8.0;
                longitudeSpan /= 8.0;
                southLatitude += secondLatitudeIndex * latitudeSpan;
                westLongitude += secondLongitudeIndex * longitudeSpan;
            }

            if (meshCode.Length >= 8)
            {
                int thirdLatitudeIndex = int.Parse(meshCode[6].ToString(), CultureInfo.InvariantCulture);
                int thirdLongitudeIndex = int.Parse(meshCode[7].ToString(), CultureInfo.InvariantCulture);
                latitudeSpan /= 10.0;
                longitudeSpan /= 10.0;
                southLatitude += thirdLatitudeIndex * latitudeSpan;
                westLongitude += thirdLongitudeIndex * longitudeSpan;
            }

            return new MeshCodeArea(
                southLatitude,
                southLatitude + latitudeSpan,
                westLongitude,
                westLongitude + longitudeSpan);
        }
    }

    private sealed record ParsedRing(
        string RingId,
        IReadOnlyList<GeodeticPoint> Vertices,
        IReadOnlyList<ResoniteFloat2>? UVs);

    private sealed record ParsedSurface(
        string PolygonId,
        ParsedRing ExteriorRing,
        IReadOnlyList<ParsedRing> InteriorRings,
        ResoniteColor BaseColor,
        string? TexturePath)
    {
        public IEnumerable<GeodeticPoint> Vertices =>
            ExteriorRing.Vertices.Concat(InteriorRings.SelectMany(static ring => ring.Vertices));
    }

    private sealed record MaterializedSurface(
        ParsedSurface Surface,
        DefaultMaterialCatalog.ResolvedMaterial Material);

    private sealed record TessellatedVertex(
        ResoniteFloat3 Position,
        ResoniteFloat2 UV);

    private sealed record TessellatedRing(
        string RingId,
        IReadOnlyList<TessellatedVertex> Vertices);

    private sealed record TessVertexData(
        ResoniteFloat3 Position,
        ResoniteFloat2 UV);

    private sealed record GeodeticPoint(
        double Latitude,
        double Longitude,
        double Altitude);

    private sealed record CoordinateReferenceSystem(
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
        string? TexturePath,
        IReadOnlyDictionary<string, IReadOnlyList<ResoniteFloat2>>? RingUvsByRingId);

    private sealed record TextureAssignment(
        string TexturePath,
        IReadOnlyDictionary<string, IReadOnlyList<ResoniteFloat2>> RingCoordinates);

    private sealed class AppearanceLibrary
    {
        private readonly Dictionary<string, ResoniteColor> colorsByPolygonId;
        private readonly Dictionary<string, TextureAssignment> texturesByPolygonId;

        private AppearanceLibrary(
            Dictionary<string, ResoniteColor> colorsByPolygonId,
            Dictionary<string, TextureAssignment> texturesByPolygonId)
        {
            this.colorsByPolygonId = colorsByPolygonId;
            this.texturesByPolygonId = texturesByPolygonId;
        }

        public static AppearanceLibrary Parse(
            XDocument document,
            string sourceDirectory,
            string datasetRoot)
        {
            Dictionary<string, ResoniteColor> colorsByPolygonId = new(StringComparer.Ordinal);
            Dictionary<string, TextureAssignment> texturesByPolygonId = new(StringComparer.Ordinal);

            foreach (XElement textureElement in document.Descendants(App + "ParameterizedTexture"))
            {
                string? imageUri = textureElement.Element(App + "imageURI")?.Value.Trim();
                string? resolvedTexturePath = ResolveTexturePath(sourceDirectory, datasetRoot, imageUri);
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

            return new AppearanceLibrary(colorsByPolygonId, texturesByPolygonId);
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

            return new SurfaceAppearance(baseColor, textureAssignment.TexturePath, textureAssignment.RingCoordinates);
        }

        private static string? ResolveTexturePath(
            string sourceDirectory,
            string datasetRoot,
            string? imageUri)
        {
            if (string.IsNullOrWhiteSpace(imageUri))
            {
                return null;
            }

            string absoluteTexturePath = Path.GetFullPath(Path.Combine(sourceDirectory, imageUri));
            if (!File.Exists(absoluteTexturePath))
            {
                return null;
            }

            return NormalizePath(Path.GetRelativePath(datasetRoot, absoluteTexturePath));
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
    }
}
