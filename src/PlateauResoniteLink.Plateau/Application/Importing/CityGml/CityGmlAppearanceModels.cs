using System.Collections.Generic;

using System.Xml.Linq;

using PlateauResoniteLink.Core.Application.Importing.Contracts;

namespace PlateauResoniteLink.Plateau.Application.Importing.CityGml;

internal sealed record CityGmlMaterialAttributes(
    ColorRgba DiffuseColor,
    double? AmbientIntensity,
    ColorRgba? EmissiveColor,
    ColorRgba? SpecularColor,
    double? Shininess,
    double? Transparency);

internal sealed record CityGmlParameterizedTexture(
    string ResolvedTexturePath,
    string? MimeType,
    string? TextureType,
    string? WrapMode,
    ColorRgba? BorderColor,
    IReadOnlyDictionary<string, IReadOnlyList<Float2>> RingCoordinates);

internal sealed record CityGmlGeoreferencedTexture(
    string? ResolvedTexturePath,
    string? MimeType,
    string? TextureType,
    string? WrapMode,
    ColorRgba? BorderColor,
    string? ReferencePoint,
    string? Orientation);

internal sealed record CityGmlResolvedAppearance(
    ColorRgba BaseColor,
    TexturePayload? TexturePayload,
    CityGmlMaterialAttributes? MaterialAttributes = null,
    CityGmlParameterizedTexture? ParameterizedTexture = null,
    CityGmlGeoreferencedTexture? GeoreferencedTexture = null);

internal sealed record CityGmlLodSelection(
    XElement[] SurfaceElements,
    int? LodLevel);
