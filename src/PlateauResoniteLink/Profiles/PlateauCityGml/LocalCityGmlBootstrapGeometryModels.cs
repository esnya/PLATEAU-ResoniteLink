using System.Xml.Linq;

using GeographicLib;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record GeodeticPoint(
    double Latitude,
    double Longitude,
    double Altitude)
{
    internal LocalCityGmlObjectProjection.GeodeticPoint ToLegacy()
    {
        return new LocalCityGmlObjectProjection.GeodeticPoint(Latitude, Longitude, Altitude);
    }

    internal static GeodeticPoint FromLegacy(LocalCityGmlObjectProjection.GeodeticPoint point)
    {
        return new GeodeticPoint(point.Latitude, point.Longitude, point.Altitude);
    }
}

internal sealed record TerrainHeightTriangle(
    GeodeticPoint Vertex0,
    GeodeticPoint Vertex1,
    GeodeticPoint Vertex2)
{
    internal LocalCityGmlObjectProjection.TerrainHeightTriangle ToLegacy()
    {
        return new LocalCityGmlObjectProjection.TerrainHeightTriangle(
            Vertex0.ToLegacy(),
            Vertex1.ToLegacy(),
            Vertex2.ToLegacy());
    }

    internal static TerrainHeightTriangle FromLegacy(LocalCityGmlObjectProjection.TerrainHeightTriangle triangle)
    {
        return new TerrainHeightTriangle(
            GeodeticPoint.FromLegacy(triangle.Vertex0),
            GeodeticPoint.FromLegacy(triangle.Vertex1),
            GeodeticPoint.FromLegacy(triangle.Vertex2));
    }
}

internal sealed record DemTerrainBounds(
    double SouthLatitude,
    double NorthLatitude,
    double WestLongitude,
    double EastLongitude)
{
    internal static DemTerrainBounds FromLegacy(MeshCodeBounds bounds)
    {
        return new DemTerrainBounds(
            bounds.SouthLatitude,
            bounds.NorthLatitude,
            bounds.WestLongitude,
            bounds.EastLongitude);
    }

    internal MeshCodeBounds ToLegacy()
    {
        return new MeshCodeBounds(
            SouthLatitude,
            NorthLatitude,
            WestLongitude,
            EastLongitude);
    }
}

internal sealed record CoordinateReferenceSystem(
    string SrsName,
    Geocentric? Geocentric,
    string CompatibilityKey)
{
    private static readonly XNamespace Gml = "http://www.opengis.net/gml";

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

    internal static CoordinateReferenceSystem FromLegacy(LocalCityGmlObjectProjection.CoordinateReferenceSystem referenceSystem)
    {
        return new CoordinateReferenceSystem(referenceSystem.SrsName, referenceSystem.Geocentric, referenceSystem.CompatibilityKey);
    }

    internal LocalCityGmlObjectProjection.CoordinateReferenceSystem ToLegacy()
    {
        return new LocalCityGmlObjectProjection.CoordinateReferenceSystem(
            SrsName,
            Geocentric,
            CompatibilityKey);
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
            return (new Geocentric(Ellipsoid.GRS80), "jgd2000");
        }

        return (new Geocentric(Ellipsoid.GRS80), srsName.Trim());
    }
}

internal sealed class TerrainHeightSampler
{
    private readonly LocalCityGmlObjectProjection.TerrainHeightSampler legacy;

    internal TerrainHeightSampler(LocalCityGmlObjectProjection.TerrainHeightSampler legacy)
    {
        this.legacy = legacy;
    }

    internal static TerrainHeightSampler? FromLegacy(LocalCityGmlObjectProjection.TerrainHeightSampler? terrainHeightSampler)
    {
        return terrainHeightSampler is null ? null : new TerrainHeightSampler(terrainHeightSampler);
    }

    internal LocalCityGmlObjectProjection.TerrainHeightSampler ToLegacy()
    {
        return legacy;
    }

    internal static TerrainHeightSampler Create(
        IReadOnlyCollection<TerrainHeightTriangle> terrainTriangles,
        GeodeticPoint globalOriginPoint,
        Geocentric geocentric)
    {
        return new TerrainHeightSampler(
            LocalCityGmlObjectProjection.TerrainHeightSampler.Create(
                terrainTriangles.Select(static triangle => triangle.ToLegacy()).ToArray(),
                globalOriginPoint.ToLegacy(),
                geocentric));
    }
}
