using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class CityGmlSourceRepresentationSelector : ICityGmlSourceRepresentationSelector
{
    private static readonly XNamespace Gml = "http://www.opengis.net/gml";

    public CityGmlSourceRepresentationSelection[] SelectSurfaceRepresentations(
        XElement cityObjectElement,
        string packageName,
        bool isMarking,
        LodFilteringStrategy lodFilteringStrategy)
    {
        ArgumentNullException.ThrowIfNull(cityObjectElement);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentNullException.ThrowIfNull(lodFilteringStrategy);

        (XElement SurfaceElement, int? SourceRepresentationIndex)[] surfaces = cityObjectElement
            .Descendants()
            .Where(element => element.Name == Gml + "Polygon" || element.Name == Gml + "Triangle")
            .Select(surfaceElement => (surfaceElement, GetSurfaceRepresentationIndex(surfaceElement, cityObjectElement)))
            .ToArray();

        int[] explicitSourceRepresentationIndexes = surfaces
            .Where(static surface => surface.SourceRepresentationIndex.HasValue)
            .Select(static surface => surface.SourceRepresentationIndex!.Value)
            .Distinct()
            .OrderDescending()
            .ToArray();
        int[] validSourceRepresentationIndexes = explicitSourceRepresentationIndexes
            .Where(sourceRepresentationIndex => sourceRepresentationIndex > 0)
            .Where(sourceRepresentationIndex => !lodFilteringStrategy.ShouldExcludeLod(packageName, sourceRepresentationIndex, isMarking))
            .ToArray();

        if (validSourceRepresentationIndexes.Length == 0)
        {
            XElement[] unclassifiedSurfaces = surfaces
                    .Where(static surface => !surface.SourceRepresentationIndex.HasValue)
                    .Where(surface => !lodFilteringStrategy.ShouldExcludeLod(packageName, surface.SourceRepresentationIndex, isMarking))
                    .Select(static surface => surface.SurfaceElement)
                    .ToArray();
            return unclassifiedSurfaces.Length == 0
                ? []
                : [new CityGmlSourceRepresentationSelection(unclassifiedSurfaces, DetailEntry.Default, null)];
        }

        return validSourceRepresentationIndexes
            .Select(sourceRepresentationIndex =>
                new CityGmlSourceRepresentationSelection(
                    surfaces
                        .Where(surface => surface.SourceRepresentationIndex == sourceRepresentationIndex)
                        .Select(static surface => surface.SurfaceElement)
                        .ToArray(),
                    DetailEntry.FromSourceRepresentationIndex(sourceRepresentationIndex),
                    sourceRepresentationIndex))
            .Where(static selection => selection.SurfaceElements.Length > 0)
            .ToArray();
    }

    private static int? GetSurfaceRepresentationIndex(XElement surfaceElement, XElement cityObjectElement)
    {
        for (XElement? ancestor = surfaceElement.Parent; ancestor is not null && ancestor != cityObjectElement; ancestor = ancestor.Parent)
        {
            if (TryParseSourceRepresentationIndex(ancestor.Name.LocalName, out int sourceRepresentationIndex))
            {
                return sourceRepresentationIndex;
            }
        }

        return null;
    }

    private static bool TryParseSourceRepresentationIndex(string localName, out int sourceRepresentationIndex)
    {
        sourceRepresentationIndex = 0;
        if (!localName.StartsWith("lod", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int digitStart = 3;
        int digitLength = 0;
        while (digitStart + digitLength < localName.Length
            && char.IsDigit(localName[digitStart + digitLength]))
        {
            digitLength++;
        }

        return digitLength > 0
            && int.TryParse(
                localName.AsSpan(digitStart, digitLength),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out sourceRepresentationIndex);
    }
}
