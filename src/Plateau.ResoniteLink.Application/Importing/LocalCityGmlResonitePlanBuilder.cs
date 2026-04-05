using System.Globalization;
using System.Xml.Linq;

using GeographicLib;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public static class LocalCityGmlResonitePlanBuilder
{
    private static readonly ResoniteColor DefaultMaterialColor = new(1.0, 1.0, 1.0, 1.0);
    private static readonly XNamespace App = "http://www.opengis.net/citygml/appearance/2.0";
    private static readonly XNamespace Bldg = "http://www.opengis.net/citygml/building/2.0";
    private static readonly XNamespace Gml = "http://www.opengis.net/gml";

    public static ResoniteConstructionPlan BuildPlan(PlateauImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SourceKind != DatasetSourceKind.Local || string.IsNullOrWhiteSpace(request.InputPath))
        {
            throw new PlateauImportValidationException(
                ["Local CityGML import requires --source local and a dataset root via --input."]);
        }

        string datasetRoot = Path.GetFullPath(request.InputPath);
        string[] sourceFiles = Directory
            .EnumerateFiles(datasetRoot, "*.gml", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}bldg{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => Path.GetFileName(path).Contains(request.MeshCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (sourceFiles.Length == 0)
        {
            throw new PlateauImportValidationException(
                [$"No local bldg CityGML files were found for mesh code '{request.MeshCode}'."]);
        }

        List<ParsedBuilding> parsedBuildings = [];
        List<string> relativeSourceFiles = [];

        foreach (string sourceFile in sourceFiles)
        {
            XDocument document = XDocument.Load(sourceFile, LoadOptions.None);
            string relativeSourceFile = NormalizePath(Path.GetRelativePath(datasetRoot, sourceFile));
            CoordinateReferenceSystem coordinateReferenceSystem = CoordinateReferenceSystem.Parse(document);
            AppearanceLibrary appearanceLibrary = AppearanceLibrary.Parse(
                document,
                Path.GetDirectoryName(sourceFile) ?? datasetRoot,
                datasetRoot);

            relativeSourceFiles.Add(relativeSourceFile);

            ParsedBuilding[] buildingsFromFile = document
                .Descendants(Bldg + "Building")
                .Select(building => ParseBuilding(building, relativeSourceFile, appearanceLibrary, coordinateReferenceSystem))
                .Where(static building => building is not null)
                .Select(static building => building!)
                .OrderBy(static building => building.SlotKey, StringComparer.Ordinal)
                .ToArray();

            parsedBuildings.AddRange(buildingsFromFile);
        }

        if (parsedBuildings.Count == 0)
        {
            throw new PlateauImportValidationException(
                [$"No building geometry was found for mesh code '{request.MeshCode}'."]);
        }

        CoordinateReferenceSystem referenceSystem = GetReferenceSystem(parsedBuildings);
        GeodeticPoint globalOriginPoint = ComputeGlobalOrigin(parsedBuildings);
        LocalCartesian? globalCartesian = referenceSystem.IsGeographic
            ? new LocalCartesian(
                globalOriginPoint.Latitude,
                globalOriginPoint.Longitude,
                globalOriginPoint.Altitude,
                referenceSystem.Geocentric)
            : null;
        ResoniteConstructionBuilding[] buildings = parsedBuildings
            .Select(building => MaterializeBuilding(building, globalOriginPoint, globalCartesian))
            .Where(static building => building.Mesh.Submeshes.Count > 0)
            .OrderBy(static building => building.SlotKey, StringComparer.Ordinal)
            .ToArray();

        if (buildings.Length == 0)
        {
            throw new PlateauImportValidationException(
                [$"No triangulated building geometry was produced for mesh code '{request.MeshCode}'."]);
        }

        return new ResoniteConstructionPlan(
            SchemaVersion: "2.0",
            WorldName: $"PLATEAU {request.Dataset} {request.MeshCode}",
            Request: request,
            SourceDataset: new PlateauSourceDataset(
                PackageName: "bldg",
                SourceFiles: relativeSourceFiles.OrderBy(path => path, StringComparer.Ordinal).ToArray()),
            LocalOrigin: new ResoniteLocalOrigin(
                Latitude: globalOriginPoint.Latitude,
                Longitude: globalOriginPoint.Longitude,
                Altitude: globalOriginPoint.Altitude),
            Buildings: buildings);
    }

    private static ParsedBuilding? ParseBuilding(
        XElement buildingElement,
        string relativeSourceFile,
        AppearanceLibrary appearanceLibrary,
        CoordinateReferenceSystem coordinateReferenceSystem)
    {
        string buildingId = GetAttribute(buildingElement, Gml + "id") ?? "building";
        string? displayName = buildingElement.Elements(Gml + "name").FirstOrDefault()?.Value.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = buildingId;
        }

        IReadOnlyList<ParsedSurface> surfaces = buildingElement
            .Descendants(Gml + "Polygon")
            .Select(polygon => ParseSurface(polygon, appearanceLibrary))
            .Where(static surface => surface is not null)
            .Select(static surface => surface!)
            .OrderBy(static surface => surface.PolygonId, StringComparer.Ordinal)
            .ToArray();

        if (surfaces.Count == 0)
        {
            return null;
        }

        string fileStem = Path.GetFileNameWithoutExtension(relativeSourceFile);
        string slotKey = SanitizeIdentifier($"{fileStem}_{buildingId}");
        return new ParsedBuilding(slotKey, displayName!, surfaces, coordinateReferenceSystem);
    }

    private static ParsedSurface? ParseSurface(XElement polygonElement, AppearanceLibrary appearanceLibrary)
    {
        XElement? exteriorRing = polygonElement
            .Descendants(Gml + "LinearRing")
            .FirstOrDefault();
        if (exteriorRing is null)
        {
            return null;
        }

        string polygonId = GetAttribute(polygonElement, Gml + "id") ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        string ringId = GetAttribute(exteriorRing, Gml + "id") ?? polygonId;

        IReadOnlyList<GeodeticPoint> vertices = ParseRingPoints(exteriorRing);
        if (vertices.Count < 3)
        {
            return null;
        }

        SurfaceAppearance appearance = appearanceLibrary.Resolve(polygonId, ringId, vertices.Count);
        return new ParsedSurface(
            PolygonId: polygonId,
            Vertices: vertices,
            BaseColor: appearance.BaseColor,
            TexturePath: appearance.TexturePath,
            UVs: appearance.UVs);
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

    private static GeodeticPoint ComputeGlobalOrigin(IEnumerable<ParsedBuilding> buildings)
    {
        List<GeodeticPoint> allPoints = buildings
            .SelectMany(building => building.Surfaces)
            .SelectMany(surface => surface.Vertices)
            .ToList();

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

    private static CoordinateReferenceSystem GetReferenceSystem(IEnumerable<ParsedBuilding> buildings)
    {
        CoordinateReferenceSystem? referenceSystem = null;

        foreach (ParsedBuilding building in buildings)
        {
            if (referenceSystem is null)
            {
                referenceSystem = building.ReferenceSystem;
                continue;
            }

            if (!string.Equals(referenceSystem.SrsName, building.ReferenceSystem.SrsName, StringComparison.Ordinal))
            {
                throw new PlateauImportValidationException(
                    [$"Mixed CityGML coordinate reference systems are not supported. Found '{referenceSystem.SrsName}' and '{building.ReferenceSystem.SrsName}'."]);
            }
        }

        return referenceSystem
            ?? throw new PlateauImportValidationException(["No CityGML coordinate reference system was resolved."]);
    }

    private static ResoniteConstructionBuilding MaterializeBuilding(
        ParsedBuilding building,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian)
    {
        List<GeodeticPoint> allPoints = building.Surfaces.SelectMany(static surface => surface.Vertices).ToList();
        double minLatitude = allPoints.Min(static point => point.Latitude);
        double maxLatitude = allPoints.Max(static point => point.Latitude);
        double minLongitude = allPoints.Min(static point => point.Longitude);
        double maxLongitude = allPoints.Max(static point => point.Longitude);
        double minAltitude = allPoints.Min(static point => point.Altitude);

        GeodeticPoint buildingOrigin = new(
            Latitude: (minLatitude + maxLatitude) / 2.0,
            Longitude: (minLongitude + maxLongitude) / 2.0,
            Altitude: minAltitude);

        LocalCartesian? buildingCartesian = building.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                buildingOrigin.Latitude,
                buildingOrigin.Longitude,
                buildingOrigin.Altitude,
                building.ReferenceSystem.Geocentric)
            : null;
        ResoniteFloat3 slotPosition = CreateResonitePosition(
            buildingOrigin,
            globalOriginPoint,
            globalCartesian);

        List<ResoniteMeshVertex> vertices = [];
        List<ResoniteMeshSubmesh> submeshes = [];
        List<ResoniteMaterialBinding> materials = [];

        IReadOnlyList<IGrouping<string, ParsedSurface>> materialGroups = building.Surfaces
            .GroupBy(
                static surface => CreateMaterialKey(surface.TexturePath, surface.BaseColor),
                StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToArray();

        for (int materialIndex = 0; materialIndex < materialGroups.Count; materialIndex++)
        {
            IGrouping<string, ParsedSurface> materialGroup = materialGroups[materialIndex];
            List<int> indices = [];

            foreach (ParsedSurface surface in materialGroup.OrderBy(static surface => surface.PolygonId, StringComparer.Ordinal))
            {
                TriangulateSurface(surface, buildingOrigin, buildingCartesian, vertices, indices);
            }

            if (indices.Count == 0)
            {
                continue;
            }

            ParsedSurface representativeSurface = materialGroup.First();
            submeshes.Add(new ResoniteMeshSubmesh(materialIndex, materialGroup.Key, indices));
            materials.Add(
                new ResoniteMaterialBinding(
                    MaterialKey: materialGroup.Key,
                    BaseColor: representativeSurface.BaseColor,
                    TexturePath: representativeSurface.TexturePath,
                    SubmeshIndices: [materialIndex]));
        }

        return new ResoniteConstructionBuilding(
            SlotKey: building.SlotKey,
            DisplayName: building.DisplayName,
            Transform: new ResoniteTransform(slotPosition),
            Mesh: new ResoniteImportedMesh(vertices, submeshes),
            Materials: materials);
    }

    private static void TriangulateSurface(
        ParsedSurface surface,
        GeodeticPoint buildingOrigin,
        LocalCartesian? buildingCartesian,
        List<ResoniteMeshVertex> vertices,
        List<int> indices)
    {
        for (int index = 1; index < surface.Vertices.Count - 1; index++)
        {
            int[] orderedIndices = [0, index + 1, index];
            GeodeticPoint absolute0 = surface.Vertices[orderedIndices[0]];
            GeodeticPoint absolute1 = surface.Vertices[orderedIndices[1]];
            GeodeticPoint absolute2 = surface.Vertices[orderedIndices[2]];

            ResoniteFloat3 position0 = CreateResonitePosition(absolute0, buildingOrigin, buildingCartesian);
            ResoniteFloat3 position1 = CreateResonitePosition(absolute1, buildingOrigin, buildingCartesian);
            ResoniteFloat3 position2 = CreateResonitePosition(absolute2, buildingOrigin, buildingCartesian);
            ResoniteFloat3? normal = ComputeNormal(position0, position1, position2);

            if (normal is null)
            {
                continue;
            }

            int baseIndex = vertices.Count;
            vertices.Add(new ResoniteMeshVertex(position0, normal, GetUV(surface.UVs, orderedIndices[0])));
            vertices.Add(new ResoniteMeshVertex(position1, normal, GetUV(surface.UVs, orderedIndices[1])));
            vertices.Add(new ResoniteMeshVertex(position2, normal, GetUV(surface.UVs, orderedIndices[2])));

            indices.Add(baseIndex);
            indices.Add(baseIndex + 1);
            indices.Add(baseIndex + 2);
        }
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

    private static ResoniteFloat2 GetUV(IReadOnlyList<ResoniteFloat2>? uvs, int index)
    {
        if (uvs is null || index >= uvs.Count)
        {
            return new ResoniteFloat2(0.0, 0.0);
        }

        return uvs[index];
    }

    private static string CreateMaterialKey(string? texturePath, ResoniteColor color)
    {
        string colorKey = string.Create(
            CultureInfo.InvariantCulture,
            $"{color.R:0.######},{color.G:0.######},{color.B:0.######},{color.A:0.######}");

        return texturePath is null
            ? $"color:{colorKey}"
            : $"texture:{texturePath.ToLowerInvariant()}|color:{colorKey}";
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

    private sealed record ParsedBuilding(
        string SlotKey,
        string DisplayName,
        IReadOnlyList<ParsedSurface> Surfaces,
        CoordinateReferenceSystem ReferenceSystem);

    private sealed record ParsedSurface(
        string PolygonId,
        IReadOnlyList<GeodeticPoint> Vertices,
        ResoniteColor BaseColor,
        string? TexturePath,
        IReadOnlyList<ResoniteFloat2>? UVs);

    private sealed record GeodeticPoint(
        double Latitude,
        double Longitude,
        double Altitude);

    private sealed record CoordinateReferenceSystem(
        string SrsName,
        Geocentric? Geocentric)
    {
        public bool IsGeographic => Geocentric is not null;

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
                return new CoordinateReferenceSystem("local-cartesian", null);
            }

            Geocentric geocentric = ResolveGeocentric(srsName);
            return new CoordinateReferenceSystem(srsName, geocentric);
        }

        private static Geocentric ResolveGeocentric(string srsName)
        {
            if (srsName.EndsWith("/6697", StringComparison.Ordinal)
                || srsName.EndsWith("EPSG:6697", StringComparison.OrdinalIgnoreCase))
            {
                return new Geocentric(Ellipsoid.GRS80);
            }

            if (srsName.EndsWith("/4979", StringComparison.Ordinal)
                || srsName.EndsWith("EPSG:4979", StringComparison.OrdinalIgnoreCase)
                || srsName.EndsWith("/4326", StringComparison.Ordinal)
                || srsName.EndsWith("EPSG:4326", StringComparison.OrdinalIgnoreCase))
            {
                return Geocentric.WGS84;
            }

            throw new PlateauImportValidationException(
                [$"Unsupported CityGML CRS '{srsName}'. Only geographic 3D CRS values currently used by PLATEAU are supported."]);
        }
    }

    private sealed record SurfaceAppearance(
        ResoniteColor BaseColor,
        string? TexturePath,
        IReadOnlyList<ResoniteFloat2>? UVs);

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

        public SurfaceAppearance Resolve(string polygonId, string ringId, int vertexCount)
        {
            ResoniteColor baseColor = colorsByPolygonId.TryGetValue(polygonId, out ResoniteColor? color)
                ? color
                : DefaultMaterialColor;

            if (!texturesByPolygonId.TryGetValue(polygonId, out TextureAssignment? textureAssignment))
            {
                return new SurfaceAppearance(baseColor, null, null);
            }

            if (!textureAssignment.RingCoordinates.TryGetValue(ringId, out IReadOnlyList<ResoniteFloat2>? textureCoordinates)
                || textureCoordinates.Count != vertexCount)
            {
                return new SurfaceAppearance(baseColor, null, null);
            }

            return new SurfaceAppearance(baseColor, textureAssignment.TexturePath, textureCoordinates);
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
