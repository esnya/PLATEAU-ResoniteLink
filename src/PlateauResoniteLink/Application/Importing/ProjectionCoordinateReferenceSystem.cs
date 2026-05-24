using System;
using System.Linq;
using System.Xml.Linq;

using GeographicLib;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record ProjectionCoordinateReferenceSystem(
    string SrsName,
    Geocentric? Geocentric,
    string CompatibilityKey)
{
    private static readonly XNamespace Gml = "http://www.opengis.net/gml";

    public bool IsGeographic => Geocentric is not null;

    public bool IsCompatibleWith(ProjectionCoordinateReferenceSystem other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return string.Equals(CompatibilityKey, other.CompatibilityKey, StringComparison.Ordinal);
    }

    public static ProjectionCoordinateReferenceSystem Parse(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        string? srsName = document
            .Descendants(Gml + "Envelope")
            .Attributes("srsName")
            .Select(static attribute => attribute.Value.Trim())
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

        return Parse(srsName);
    }

    public static ProjectionCoordinateReferenceSystem Parse(string? srsName)
    {
        if (string.IsNullOrWhiteSpace(srsName))
        {
            return new ProjectionCoordinateReferenceSystem("local-cartesian", null, "local-cartesian");
        }

        (Geocentric geocentric, string compatibilityKey) = ResolveGeocentric(srsName);
        return new ProjectionCoordinateReferenceSystem(srsName, geocentric, compatibilityKey);
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
