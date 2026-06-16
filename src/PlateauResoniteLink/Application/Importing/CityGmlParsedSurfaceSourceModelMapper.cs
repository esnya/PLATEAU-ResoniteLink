using System;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record CityGmlParsedSurfaceSource(
    ParsedSurfaceSemantic Semantic,
    ParsedRing ExteriorRing,
    ParsedRing[] InteriorRings,
    CityGmlResolvedAppearance Appearance);

internal static class CityGmlParsedSurfaceSourceModelMapper
{
    internal static ParsedSurface ToParsedSurface(
        string packageName,
        CityGmlParsedSurfaceSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        ParsedSurface surface = new(
            Semantic: source.Semantic,
            ExteriorRing: source.ExteriorRing,
            InteriorRings: source.InteriorRings,
            BaseColor: ToInternalColor(source.Appearance.BaseColor),
            TexturePayload: source.Appearance.TexturePayload,
            OpticalProperties: CreateMaterialOpticalProperties(source.Appearance.MaterialAttributes));

        return ApplyPackageDefaults(packageName, surface);
    }

    private static ParsedSurface ApplyPackageDefaults(string packageName, ParsedSurface surface)
    {
        _ = packageName;
        return surface;
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

    private static ColorRgba ToInternalColor(ColorRgba value) => new(value.R, value.G, value.B, value.A);
}
