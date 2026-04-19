using System.Xml.Linq;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed record CityGmlMaterialAttributes(
    ResoniteColor DiffuseColor,
    double? AmbientIntensity,
    ResoniteColor? EmissiveColor,
    ResoniteColor? SpecularColor,
    double? Shininess,
    double? Transparency);

internal sealed record CityGmlParameterizedTexture(
    string ResolvedTexturePath,
    string? MimeType,
    string? TextureType,
    string? WrapMode,
    ResoniteColor? BorderColor,
    IReadOnlyDictionary<string, IReadOnlyList<ResoniteFloat2>> RingCoordinates);

internal sealed record CityGmlGeoreferencedTexture(
    string? ResolvedTexturePath,
    string? MimeType,
    string? TextureType,
    string? WrapMode,
    ResoniteColor? BorderColor,
    string? ReferencePoint,
    string? Orientation);

internal sealed record CityGmlResolvedAppearance(
    ResoniteColor BaseColor,
    ResoniteTexturePayload? TexturePayload,
    IReadOnlyDictionary<string, IReadOnlyList<ResoniteFloat2>>? RingUvsByRingId,
    CityGmlMaterialAttributes? MaterialAttributes = null,
    CityGmlParameterizedTexture? ParameterizedTexture = null,
    CityGmlGeoreferencedTexture? GeoreferencedTexture = null);

internal sealed record CityGmlLodSelection(
    XElement[] SurfaceElements,
    int? LodLevel);
