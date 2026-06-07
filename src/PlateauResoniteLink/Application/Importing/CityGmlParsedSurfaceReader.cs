using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityGmlParsedSurfaceReader
{
    private static readonly XNamespace Gml = "http://www.opengis.net/gml";

    internal static ParsedSurface? TryParse(XElement polygonElement, ICityGmlAppearanceStore appearanceStore)
    {
        ArgumentNullException.ThrowIfNull(polygonElement);
        ArgumentNullException.ThrowIfNull(appearanceStore);

        XElement? exteriorRing = polygonElement
            .Element(Gml + "exterior")
            ?.Element(Gml + "LinearRing");
        if (exteriorRing is null)
        {
            return null;
        }

        string? polygonId = GetAttribute(polygonElement, Gml + "id");
        CityGmlResolvedAppearance appearance = polygonId is null
            ? new CityGmlResolvedAppearance(
                new ColorRgba(1.0, 1.0, 1.0, 1.0),
                TexturePayload: null)
            : appearanceStore.Resolve(polygonId);
        if (TryParseRing(
            exteriorRing,
            polygonId,
            fallbackRingId: polygonId,
            appearanceStore) is not { } exteriorParsedRing)
        {
            return null;
        }

        ParsedRing[] interiorRings = ParseInteriorRings(polygonElement, polygonId, appearanceStore);

        return new ParsedSurface(
            Semantic: ParseSurfaceSemantic(polygonElement),
            ExteriorRing: exteriorParsedRing,
            InteriorRings: interiorRings,
            BaseColor: ToInternalColor(appearance.BaseColor),
            TexturePayload: appearance.TexturePayload,
            OpticalProperties: CreateMaterialOpticalProperties(appearance.MaterialAttributes));
    }

    internal static ParsedSurface ApplyPackageDefaults(string packageName, ParsedSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        return surface;
    }

    private static ParsedRing[] ParseInteriorRings(
        XElement polygonElement,
        string? polygonId,
        ICityGmlAppearanceStore appearanceStore)
    {
        List<ParsedRing> rings = [];
        foreach (XElement interiorElement in polygonElement.Elements(Gml + "interior"))
        {
            if (TryParseRing(
                interiorElement.Element(Gml + "LinearRing"),
                polygonId,
                fallbackRingId: null,
                appearanceStore) is { } ring)
            {
                rings.Add(ring);
            }
        }

        return rings.ToArray();
    }

    private static ParsedRing? TryParseRing(
        XElement? ringElement,
        string? polygonId,
        string? fallbackRingId,
        ICityGmlAppearanceStore appearanceStore)
    {
        if (ringElement is null)
        {
            return null;
        }

        string? ringId = GetAttribute(ringElement, Gml + "id")
            ?? fallbackRingId
            ?? polygonId;
        GeodeticPoint[] vertices = ParseRingPoints(ringElement);
        if (vertices.Length < 3)
        {
            return null;
        }

        IReadOnlyList<Float2>? uvs = polygonId is not null && ringId is not null
            ? appearanceStore.ResolveRingUvs(polygonId, ringId, vertices.Length)
            : null;

        return new ParsedRing(vertices, uvs);
    }

    private static GeodeticPoint[] ParseRingPoints(XElement ringElement)
    {
        List<double> ordinates = [];
        XElement? posListElement = ringElement.Element(Gml + "posList");
        if (posListElement is not null)
        {
            ordinates.AddRange(CityGmlCoordinateTextParser.ParseDoubles(posListElement.Value));
        }
        else
        {
            foreach (XElement posElement in ringElement.Elements(Gml + "pos"))
            {
                ordinates.AddRange(CityGmlCoordinateTextParser.ParseDoubles(posElement.Value));
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

    private static ColorRgba ToInternalColor(ColorRgba value) => new(value.R, value.G, value.B, value.A);
}
