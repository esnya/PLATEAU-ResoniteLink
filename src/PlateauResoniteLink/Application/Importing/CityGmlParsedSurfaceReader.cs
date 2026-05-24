using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityGmlParsedSurfaceReader
{
    private static readonly XNamespace Gml = "http://www.opengis.net/gml";

    public static ParsedSurface? Parse(XElement polygonElement, ICityGmlAppearanceStore appearanceStore)
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

    public static ParsedSurface ApplyPackageDefaults(string packageName, ParsedSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        return string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase)
            && surface.TexturePayload is null
            ? surface with { UsesGeneratedDemTexture = true }
            : surface;
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

    private static string CreateStableElementId(string prefix, XElement element)
    {
        byte[] payload = Encoding.UTF8.GetBytes(element.ToString(SaveOptions.DisableFormatting));
        byte[] hash = SHA256.HashData(payload);
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{prefix}_{Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant()}");
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
        return element.Attribute(attributeName)?.Value.Trim();
    }

    private static bool AreSamePoint(GeodeticPoint left, GeodeticPoint right)
    {
        return Math.Abs(left.Latitude - right.Latitude) < 1e-12
            && Math.Abs(left.Longitude - right.Longitude) < 1e-12
            && Math.Abs(left.Altitude - right.Altitude) < 1e-9;
    }

    private static ColorRgba ToInternalColor(ColorRgba value) => new(value.R, value.G, value.B, value.A);
}
