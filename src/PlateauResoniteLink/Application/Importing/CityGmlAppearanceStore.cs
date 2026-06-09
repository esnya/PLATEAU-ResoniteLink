using System;
using System.Collections.Generic;
using System.Linq;

using System.Xml.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal interface ICityGmlAppearanceStore
{
    void LoadFromDocument(XDocument document);

    void ApplyAppearanceElement(XElement appearanceElement);

    CityGmlResolvedAppearance Resolve(string polygonId);

    IReadOnlyList<Float2>? ResolveRingUvs(string polygonId, string ringId, int vertexCount);
}

internal sealed class CityGmlAppearanceStore : ICityGmlAppearanceStore
{
    private static readonly ColorRgba DefaultMaterialColor = new(1.0, 1.0, 1.0, 1.0);
    private static readonly XNamespace App = "http://www.opengis.net/citygml/appearance/2.0";

    private readonly Dictionary<string, CityGmlMaterialAttributes> materialAttributesByPolygonId = new(StringComparer.Ordinal);
    private readonly IPlateauDatasetContentSource datasetSource;
    private readonly string sourceFileRelativePath;
    private readonly Dictionary<string, TexturePayload> texturePayloadsByResolvedPath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CityGmlParameterizedTexture> parameterizedTexturesByPolygonId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CityGmlGeoreferencedTexture> georeferencedTexturesByPolygonId = new(StringComparer.Ordinal);

    internal CityGmlAppearanceStore(
        string sourceFileRelativePath,
        IPlateauDatasetContentSource datasetSource)
    {
        this.sourceFileRelativePath = sourceFileRelativePath;
        this.datasetSource = datasetSource;
    }

    internal static CityGmlAppearanceStore Create(
        string sourceFileRelativePath,
        IPlateauDatasetContentSource datasetSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileRelativePath);
        ArgumentNullException.ThrowIfNull(datasetSource);
        return new CityGmlAppearanceStore(sourceFileRelativePath, datasetSource);
    }

    public void LoadFromDocument(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (XElement appearanceElement in document.Descendants()
                     .Where(element => string.Equals(element.Name.NamespaceName, App.NamespaceName, StringComparison.Ordinal)))
        {
            if (IsSupportedAppearanceElement(appearanceElement))
            {
                ApplyAppearanceElement(appearanceElement);
            }
        }
    }

    public void ApplyAppearanceElement(XElement appearanceElement)
    {
        ArgumentNullException.ThrowIfNull(appearanceElement);

        if (!string.Equals(appearanceElement.Name.NamespaceName, App.NamespaceName, StringComparison.Ordinal))
        {
            return;
        }

        switch (appearanceElement.Name.LocalName)
        {
            case "ParameterizedTexture":
                ApplyParameterizedTexture(appearanceElement);
                break;
            case "X3DMaterial":
                ApplyX3DMaterial(appearanceElement);
                break;
            case "GeoreferencedTexture":
                ApplyGeoreferencedTexture(appearanceElement);
                break;
        }
    }

    public CityGmlResolvedAppearance Resolve(string polygonId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(polygonId);

        CityGmlMaterialAttributes? materialAttributes = materialAttributesByPolygonId.GetValueOrDefault(polygonId);
        ColorRgba baseColor = ApplyTransparency(
            materialAttributes?.DiffuseColor ?? DefaultMaterialColor,
            materialAttributes?.Transparency);

        CityGmlParameterizedTexture? parameterizedTexture = parameterizedTexturesByPolygonId.GetValueOrDefault(polygonId);
        TexturePayload? texturePayload = null;
        if (parameterizedTexture is not null)
        {
            texturePayload = LoadTexturePayload(parameterizedTexture.ResolvedTexturePath);
        }

        return new CityGmlResolvedAppearance(
            BaseColor: baseColor,
            TexturePayload: texturePayload,
            MaterialAttributes: materialAttributes,
            ParameterizedTexture: parameterizedTexture,
            GeoreferencedTexture: georeferencedTexturesByPolygonId.GetValueOrDefault(polygonId));
    }

    public IReadOnlyList<Float2>? ResolveRingUvs(string polygonId, string ringId, int vertexCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(polygonId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ringId);

        return parameterizedTexturesByPolygonId.GetValueOrDefault(polygonId)
                ?.RingCoordinates.GetValueOrDefault(ringId) is { } ringUvs
            && ringUvs.Count == vertexCount
                ? ringUvs
                : null;
    }

    private void ApplyParameterizedTexture(XElement textureElement)
    {
        string? imageUri = textureElement.Element(App + "imageURI")?.Value.Trim();
        string? resolvedTexturePath = ResolveTexturePath(imageUri);
        if (resolvedTexturePath is null)
        {
            return;
        }

        string? mimeType = textureElement.Element(App + "mimeType")?.Value.Trim();
        string? textureType = textureElement.Element(App + "textureType")?.Value.Trim();
        string? wrapMode = textureElement.Element(App + "wrapMode")?.Value.Trim();
        ColorRgba? borderColor = TryParseColor(textureElement.Element(App + "borderColor")?.Value);

        foreach (XElement targetElement in textureElement.Elements(App + "target"))
        {
            string? polygonId = StripReferencePrefix(targetElement.Attribute("uri")?.Value);
            if (string.IsNullOrWhiteSpace(polygonId))
            {
                continue;
            }

            Dictionary<string, IReadOnlyList<Float2>> ringCoordinates = new(StringComparer.Ordinal);
            foreach (XElement textureCoordinatesElement in targetElement.Descendants(App + "textureCoordinates"))
            {
                string? ringId = StripReferencePrefix(textureCoordinatesElement.Attribute("ring")?.Value);
                if (string.IsNullOrWhiteSpace(ringId))
                {
                    continue;
                }

                List<Float2> coordinates = CityGmlCoordinateTextParser.ParseTextureCoordinates(textureCoordinatesElement.Value);
                if (coordinates.Count > 0)
                {
                    ringCoordinates[ringId] = coordinates;
                }
            }

            parameterizedTexturesByPolygonId[polygonId] = new CityGmlParameterizedTexture(
                resolvedTexturePath,
                mimeType,
                textureType,
                wrapMode,
                borderColor,
                ringCoordinates);
        }
    }

    private void ApplyX3DMaterial(XElement materialElement)
    {
        CityGmlMaterialAttributes materialAttributes = new(
            DiffuseColor: ParseColor(materialElement.Element(App + "diffuseColor")?.Value, DefaultMaterialColor),
            AmbientIntensity: TryParseDouble(materialElement.Element(App + "ambientIntensity")?.Value),
            EmissiveColor: TryParseColor(materialElement.Element(App + "emissiveColor")?.Value),
            SpecularColor: TryParseColor(materialElement.Element(App + "specularColor")?.Value),
            Shininess: TryParseDouble(materialElement.Element(App + "shininess")?.Value),
            Transparency: TryParseDouble(materialElement.Element(App + "transparency")?.Value));

        foreach (XElement targetElement in materialElement.Elements(App + "target"))
        {
            string? polygonId = StripReferencePrefix(targetElement.Attribute("uri")?.Value);
            if (!string.IsNullOrWhiteSpace(polygonId))
            {
                materialAttributesByPolygonId[polygonId] = materialAttributes;
            }
        }
    }

    private void ApplyGeoreferencedTexture(XElement textureElement)
    {
        string? imageUri = textureElement.Element(App + "imageURI")?.Value.Trim();
        string? resolvedTexturePath = ResolveTexturePath(imageUri);
        string? mimeType = textureElement.Element(App + "mimeType")?.Value.Trim();
        string? textureType = textureElement.Element(App + "textureType")?.Value.Trim();
        string? wrapMode = textureElement.Element(App + "wrapMode")?.Value.Trim();
        ColorRgba? borderColor = TryParseColor(textureElement.Element(App + "borderColor")?.Value);
        string? referencePoint = textureElement.Element(App + "referencePoint")?.Value.Trim();
        string? orientation = textureElement.Element(App + "orientation")?.Value.Trim();

        foreach (XElement targetElement in textureElement.Elements(App + "target"))
        {
            string? polygonId = StripReferencePrefix(targetElement.Attribute("uri")?.Value);
            if (!string.IsNullOrWhiteSpace(polygonId))
            {
                georeferencedTexturesByPolygonId[polygonId] = new CityGmlGeoreferencedTexture(
                    resolvedTexturePath,
                    mimeType,
                    textureType,
                    wrapMode,
                    borderColor,
                    referencePoint,
                    orientation);
            }
        }
    }

    private TexturePayload? LoadTexturePayload(string resolvedTexturePath)
    {
        if (!texturePayloadsByResolvedPath.TryGetValue(resolvedTexturePath, out TexturePayload? texturePayload))
        {
            texturePayload = new EncodedImageTexturePayload(
                null,
                null,
                "sRGB",
                TextureImportSourceFactory.CreateDatasetEncodedImage(
                    datasetSource,
                    resolvedTexturePath,
                    "sRGB"));
            texturePayloadsByResolvedPath[resolvedTexturePath] = texturePayload;
        }

        return texturePayload;
    }

    private string? ResolveTexturePath(string? imageUri)
    {
        if (string.IsNullOrWhiteSpace(imageUri))
        {
            return null;
        }

        string? resolvedTexturePath = datasetSource.ResolveRelativePath(
            sourceFileRelativePath,
            imageUri);
        if (resolvedTexturePath is null || !datasetSource.FileExists(resolvedTexturePath))
        {
            return null;
        }

        return resolvedTexturePath;
    }

    private static bool IsSupportedAppearanceElement(XElement appearanceElement)
    {
        return appearanceElement.Name == App + "ParameterizedTexture"
            || appearanceElement.Name == App + "X3DMaterial"
            || appearanceElement.Name == App + "GeoreferencedTexture";
    }

    private static string? StripReferencePrefix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.StartsWith('#')
            ? value[1..]
            : value;
    }

    private static double? TryParseDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        double[]? values = TryParseDoubles(value);
        if (values is null)
        {
            return null;
        }

        return values.Length == 0 ? null : values[0];
    }

    private static ColorRgba ParseColor(string? value, ColorRgba fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        double[]? values = TryParseDoubles(value);
        if (values is null)
        {
            return fallback;
        }

        if (values.Length < 3)
        {
            return fallback;
        }

        return new ColorRgba(
            R: values[0],
            G: values[1],
            B: values[2],
            A: values.Length >= 4 ? values[3] : 1.0);
    }

    private static ColorRgba? TryParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        double[]? values = TryParseDoubles(value);
        if (values is null)
        {
            return null;
        }

        if (values.Length < 3)
        {
            return null;
        }

        return new ColorRgba(
            R: values[0],
            G: values[1],
            B: values[2],
            A: values.Length >= 4 ? values[3] : 1.0);
    }

    private static ColorRgba ApplyTransparency(ColorRgba color, double? transparency)
    {
        if (!transparency.HasValue)
        {
            return color;
        }

        double opacity = 1.0 - Math.Clamp(transparency.Value, 0.0, 1.0);
        return color with
        {
            A = Math.Clamp(color.A * opacity, 0.0, 1.0),
        };
    }

    private static double[]? TryParseDoubles(string value)
    {
        try
        {
            return CityGmlCoordinateTextParser.ParseDoubles(value);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
