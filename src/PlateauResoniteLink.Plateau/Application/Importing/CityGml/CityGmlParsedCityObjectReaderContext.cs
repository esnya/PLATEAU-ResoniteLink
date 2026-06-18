using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Application.Importing.Plateau;

namespace PlateauResoniteLink.Application.Importing.CityGml;

internal sealed record CityGmlParsedCityObjectReaderContext(
    string PackageName,
    string RelativeSourceFile,
    string ObjectId,
    string DisplayName,
    bool IsMarking,
    string ResolvedActualMeshCode,
    CityGmlLodSelection LodSelection,
    bool IncludeByPattern,
    bool ShouldExcludeByLod,
    string SlotKey)
{
    private static readonly XNamespace Gml = "http://www.opengis.net/gml";

    internal XElement[] PreferredSurfaceElements => LodSelection.SurfaceElements;

    internal int? LodLevel => LodSelection.LodLevel;

    internal bool ShouldInclude => IncludeByPattern && !ShouldExcludeByLod;

    internal static CityGmlParsedCityObjectReaderContext Create(
        XElement cityObjectElement,
        string packageName,
        string relativeSourceFile,
        string actualMeshCode,
        bool sharedAcrossMeshCodes,
        ICityGmlLodSelector lodSelector,
        LodFilteringStrategy lodFilteringStrategy)
    {
        string objectTypeName = cityObjectElement.Name.LocalName;
        string objectId = GetAttribute(cityObjectElement, Gml + "id") ?? objectTypeName;
        string displayName = cityObjectElement.Elements(Gml + "name").FirstOrDefault()?.Value.Trim() ?? objectId;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = objectId;
        }

        bool isMarking = displayName.Contains("Marking", StringComparison.OrdinalIgnoreCase)
            || objectId.Contains("Marking", StringComparison.OrdinalIgnoreCase)
            || objectId.Contains("_road_marking", StringComparison.Ordinal);

        string resolvedActualMeshCode = CityGmlMeshCodeBoundsFilter.ResolveActualMeshCode(
            packageName,
            displayName,
            objectId,
            actualMeshCode,
            sharedAcrossMeshCodes);

        CityGmlLodSelection lodSelection = lodSelector.SelectPreferredSurfaceElements(
            cityObjectElement,
            packageName,
            isMarking,
            lodFilteringStrategy);

        bool includeByPattern = lodFilteringStrategy.ShouldIncludeByPattern(packageName, objectId, isMarking);
        bool shouldExcludeByLod = lodSelection.SurfaceElements.Length == 0
            && lodFilteringStrategy.ShouldExcludeLod(packageName, lodSelection.LodLevel, isMarking);

        string fileStem = Path.GetFileNameWithoutExtension(relativeSourceFile);
        string slotKey = SanitizeIdentifier($"{packageName}_{fileStem}_{objectId}");

        return new CityGmlParsedCityObjectReaderContext(
            packageName,
            relativeSourceFile,
            objectId,
            displayName,
            isMarking,
            resolvedActualMeshCode,
            lodSelection,
            includeByPattern,
            shouldExcludeByLod,
            slotKey);
    }

    private static string? GetAttribute(XElement element, XName attributeName)
    {
        return element.Attribute(attributeName)?.Value;
    }

    private static string SanitizeIdentifier(string value)
    {
        return string.Concat(
            value.Select(character => char.IsLetterOrDigit(character) ? character : '_'));
    }
}
