using System;
using System.Collections.Generic;
using System.Linq;

using System.Xml.Linq;

using GeographicLib;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record GeodeticPoint(
    double Latitude,
    double Longitude,
    double Altitude)
{
    internal LocalCityGmlObjectProjection.GeodeticPoint ToProjectionModel()
    {
        return new LocalCityGmlObjectProjection.GeodeticPoint(Latitude, Longitude, Altitude);
    }

    internal static GeodeticPoint FromProjectionModel(LocalCityGmlObjectProjection.GeodeticPoint point)
    {
        return new GeodeticPoint(point.Latitude, point.Longitude, point.Altitude);
    }
}

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
    GeodeticPoint Vertex2)
{
    internal ProjectionTerrainHeightTriangle ToProjectionModel()
    {
        return new ProjectionTerrainHeightTriangle(
            Vertex0,
            Vertex1,
            Vertex2);
    }

    internal static TerrainHeightTriangle FromProjectionModel(ProjectionTerrainHeightTriangle triangle)
    {
        return new TerrainHeightTriangle(
            triangle.Vertex0,
            triangle.Vertex1,
            triangle.Vertex2);
    }
}

internal sealed record DemTerrainBounds(
    double SouthLatitude,
    double NorthLatitude,
    double WestLongitude,
    double EastLongitude)
{
    internal static DemTerrainBounds FromProjectionModel(MeshCodeBounds bounds)
    {
        return new DemTerrainBounds(
            bounds.SouthLatitude,
            bounds.NorthLatitude,
            bounds.WestLongitude,
            bounds.EastLongitude);
    }

    internal MeshCodeBounds ToProjectionModel()
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

    internal static CoordinateReferenceSystem FromProjectionModel(LocalCityGmlObjectProjection.CoordinateReferenceSystem referenceSystem)
    {
        return new CoordinateReferenceSystem(referenceSystem.SrsName, referenceSystem.Geocentric, referenceSystem.CompatibilityKey);
    }

    internal LocalCityGmlObjectProjection.CoordinateReferenceSystem ToProjectionModel()
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
    private readonly ProjectionTerrainHeightSampler projectionSampler;

    internal TerrainHeightSampler(ProjectionTerrainHeightSampler projectionSampler)
    {
        this.projectionSampler = projectionSampler;
    }

    internal static TerrainHeightSampler? FromProjectionModel(ProjectionTerrainHeightSampler? terrainHeightSampler)
    {
        return terrainHeightSampler is null ? null : new TerrainHeightSampler(terrainHeightSampler);
    }

    internal ProjectionTerrainHeightSampler ToProjectionModel()
    {
        return projectionSampler;
    }

    internal static TerrainHeightSampler Create(
        IReadOnlyCollection<TerrainHeightTriangle> terrainTriangles,
        GeodeticPoint globalOriginPoint,
        Geocentric geocentric)
    {
        return new TerrainHeightSampler(
            ProjectionTerrainHeightSampler.Create(
                terrainTriangles.Select(static triangle => triangle.ToProjectionModel()).ToArray(),
                globalOriginPoint,
                geocentric));
    }
}
