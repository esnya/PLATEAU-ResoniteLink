using System;
using System.Linq;

using System.Xml.Linq;

using GeographicLib;

using PlateauResoniteLink.Core.Application.Importing;
using PlateauResoniteLink.Core.Application.Importing.Contracts;
using PlateauResoniteLink.Plateau.Application.Importing.Plateau;

namespace PlateauResoniteLink.Plateau.Application.Importing.Source;

internal sealed record GeodeticPoint(
    double Latitude,
    double Longitude,
    double Altitude);

internal static class SceneAxisMapper
{
    public static Float3 FromEastUpNorth((double X, double Y, double Z) eastUpNorth)
    {
        return new Float3(
            X: eastUpNorth.X,
            Y: eastUpNorth.Z,
            Z: eastUpNorth.Y);
    }

    public static Float3 CreatePosition(
        double latitude,
        double longitude,
        double altitude,
        double originLatitude,
        double originLongitude,
        double originAltitude,
        LocalCartesian? cartesian)
    {
        if (cartesian is null)
        {
            return new Float3(
                X: latitude - originLatitude,
                Y: altitude - originAltitude,
                Z: longitude - originLongitude);
        }

        return FromEastUpNorth(cartesian.Forward(
            latitude,
            longitude,
            altitude));
    }
}

internal sealed record TerrainHeightTriangle(
    GeodeticPoint Vertex0,
    GeodeticPoint Vertex1,
    GeodeticPoint Vertex2);

internal sealed record DemTerrainBounds(
    double SouthLatitude,
    double NorthLatitude,
    double WestLongitude,
    double EastLongitude)
{
    internal static DemTerrainBounds FromMeshCodeBounds(MeshCodeBounds bounds)
    {
        return new DemTerrainBounds(
            bounds.SouthLatitude,
            bounds.NorthLatitude,
            bounds.WestLongitude,
            bounds.EastLongitude);
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

        if (srsName.EndsWith("/4326", StringComparison.Ordinal)
            || srsName.EndsWith("EPSG:4326", StringComparison.OrdinalIgnoreCase)
            || srsName.EndsWith("/4979", StringComparison.Ordinal)
            || srsName.EndsWith("EPSG:4979", StringComparison.OrdinalIgnoreCase))
        {
            return (Geocentric.WGS84, "wgs84");
        }

        throw new PlateauImportValidationException([$"Unsupported CityGML coordinate reference system '{srsName.Trim()}'."]);
    }
}
