using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

using ProjectionParsedRing = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.ParsedRing;
using ProjectionParsedSurface = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.ParsedSurface;
using ProjectionParsedSurfaceSemantic = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.ParsedSurfaceSemantic;
using ProjectionGeodeticPoint = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.GeodeticPoint;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityGmlParsedSurfaceReader
{
    private static readonly XNamespace Gml = "http://www.opengis.net/gml";

    internal static ProjectionParsedSurface? Parse(XElement polygonElement, ICityGmlAppearanceStore appearanceStore)
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
        ProjectionParsedRing? exteriorParsedRing = ParseRing(
            exteriorRing,
            appearance.RingUvsByRingId,
            fallbackRingId: polygonId);
        if (exteriorParsedRing is null)
        {
            return null;
        }

        ProjectionParsedRing[] interiorRings = polygonElement
            .Elements(Gml + "interior")
            .Select(interiorElement => ParseRing(
                interiorElement.Element(Gml + "LinearRing"),
                appearance.RingUvsByRingId,
                fallbackRingId: null))
            .Where(static ring => ring is not null)
            .Select(static ring => ring!)
            .ToArray();

        return new ProjectionParsedSurface(
            PolygonId: polygonId,
            Semantic: ParseSurfaceSemantic(polygonElement),
            ExteriorRing: exteriorParsedRing,
            InteriorRings: interiorRings,
            BaseColor: ToInternalColor(appearance.BaseColor),
            TexturePayload: appearance.TexturePayload,
            OpticalProperties: CreateMaterialOpticalProperties(appearance.MaterialAttributes));
    }

    internal static ProjectionParsedSurface ApplyPackageDefaults(string packageName, ProjectionParsedSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        return string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase)
            && surface.TexturePayload is null
            ? surface with { UsesGeneratedDemTexture = true }
            : surface;
    }

    private static ProjectionParsedRing? ParseRing(
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
        ProjectionGeodeticPoint[] vertices = ParseRingPoints(ringElement);
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

        return new ProjectionParsedRing(ringId, vertices, uvs);
    }

    private static ProjectionGeodeticPoint[] ParseRingPoints(XElement ringElement)
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

        List<ProjectionGeodeticPoint> points = [];
        for (int index = 0; index + 2 < ordinates.Count; index += 3)
        {
            points.Add(new ProjectionGeodeticPoint(ordinates[index], ordinates[index + 1], ordinates[index + 2]));
        }

        if (points.Count > 1 && AreSamePoint(points[0], points[^1]))
        {
            points.RemoveAt(points.Count - 1);
        }

        return points.ToArray();
    }

    private static ProjectionParsedSurfaceSemantic ParseSurfaceSemantic(XElement polygonElement)
    {
        for (XElement? ancestor = polygonElement.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            ProjectionParsedSurfaceSemantic semantic = ancestor.Name.LocalName switch
            {
                "WallSurface" or "InteriorWallSurface" => ProjectionParsedSurfaceSemantic.Wall,
                "RoofSurface" => ProjectionParsedSurfaceSemantic.Roof,
                "GroundSurface" => ProjectionParsedSurfaceSemantic.Ground,
                "ClosureSurface" => ProjectionParsedSurfaceSemantic.Closure,
                "OuterCeilingSurface" => ProjectionParsedSurfaceSemantic.OuterCeiling,
                "OuterFloorSurface" => ProjectionParsedSurfaceSemantic.OuterFloor,
                _ => ProjectionParsedSurfaceSemantic.Unknown,
            };

            if (semantic != ProjectionParsedSurfaceSemantic.Unknown)
            {
                return semantic;
            }
        }

        return ProjectionParsedSurfaceSemantic.Unknown;
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
        return element.Attribute(attributeName)?.Value;
    }

    private static bool AreSamePoint(ProjectionGeodeticPoint left, ProjectionGeodeticPoint right)
    {
        return Math.Abs(left.Latitude - right.Latitude) < 1e-8
            && Math.Abs(left.Longitude - right.Longitude) < 1e-8
            && Math.Abs(left.Altitude - right.Altitude) < 1e-8;
    }

    private static ColorRgba ToInternalColor(ColorRgba value) => new(value.R, value.G, value.B, value.A);
}
