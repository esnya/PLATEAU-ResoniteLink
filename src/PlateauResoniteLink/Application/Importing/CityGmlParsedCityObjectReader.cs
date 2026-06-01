using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityGmlParsedCityObjectReader
{
    private static readonly XNamespace Gml = "http://www.opengis.net/gml";

    internal static ParsedCityObject? Parse(
        XElement cityObjectElement,
        string packageName,
        string relativeSourceFile,
        string actualMeshCode,
        bool sharedAcrossMeshCodes,
        ICityGmlAppearanceStore appearanceStore,
        ICityGmlLodSelector lodSelector,
        CoordinateReferenceSystem coordinateReferenceSystem,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        LodFilteringStrategy lodFilteringStrategy)
    {
        string objectTypeName = cityObjectElement.Name.LocalName;
        string objectId = GetAttribute(cityObjectElement, Gml + "id") ?? objectTypeName;
        string? displayName = cityObjectElement.Elements(Gml + "name").FirstOrDefault()?.Value.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = objectId;
        }

        string resolvedActualMeshCode = CityGmlMeshCodeBoundsFilter.ResolveActualMeshCode(
            packageName,
            displayName!,
            objectId,
            actualMeshCode,
            sharedAcrossMeshCodes);
        BuildingAttributeContext buildingAttributes = BuildingAttributeParser.Parse(cityObjectElement);
        int? floorsAboveGround = BuildingAttributeQueries.TryGetKnownPositiveInteger(
            buildingAttributes,
            BuildingMetricKind.StoreysAboveGround);
        double? measuredHeightMeters = BuildingAttributeQueries.TryGetKnownPositiveMetric(
            buildingAttributes,
            BuildingMetricKind.MeasuredHeightMeters);

        bool isMarking = displayName.Contains("Marking", StringComparison.OrdinalIgnoreCase)
            || objectId.Contains("Marking", StringComparison.OrdinalIgnoreCase)
            || objectId.Contains("_road_marking", StringComparison.Ordinal);

        CityGmlLodSelection lodSelection = lodSelector.SelectPreferredSurfaceElements(
            cityObjectElement,
            packageName,
            isMarking,
            lodFilteringStrategy);
        XElement[] preferredSurfaceElements = lodSelection.SurfaceElements;
        int? lodLevel = lodSelection.LodLevel;

        if (!lodFilteringStrategy.ShouldIncludeByPattern(packageName, objectId, isMarking))
        {
            return null;
        }

        if (preferredSurfaceElements.Length == 0 && lodFilteringStrategy.ShouldExcludeLod(packageName, lodLevel, isMarking))
        {
            return null;
        }

        ParsedSurface[] surfaces = preferredSurfaceElements
            .Select(surfaceElement => CityGmlParsedSurfaceReader.Parse(surfaceElement, appearanceStore))
            .Where(static surface => surface is not null)
            .Select(static surface => surface!)
            .Select(surface => CityGmlParsedSurfaceReader.ApplyPackageDefaults(packageName, surface))
            .OrderBy(static surface => ParsedSurfaceStableSortKey.Create(surface), StringComparer.Ordinal)
            .ToArray();

        if (surfaces.Length == 0)
        {
            return null;
        }

        if (!CityGmlMeshCodeBoundsFilter.IntersectsRequestedMeshCodeBounds(
                resolvedActualMeshCode,
                sharedAcrossMeshCodes,
                coordinateReferenceSystem,
                requestedMeshCodeBounds,
                surfaces))
        {
            return null;
        }

        string fileStem = Path.GetFileNameWithoutExtension(relativeSourceFile);
        string slotKey = SanitizeIdentifier($"{packageName}_{fileStem}_{objectId}");
        return new ParsedCityObject(
            slotKey,
            displayName!,
            packageName,
            resolvedActualMeshCode,
            lodLevel,
            surfaces,
            coordinateReferenceSystem,
            relativeSourceFile,
            SharedAcrossMeshCodes: sharedAcrossMeshCodes,
            FloorsAboveGround: floorsAboveGround,
            MeasuredHeightMeters: measuredHeightMeters,
            BuildingAttributes: buildingAttributes,
            SourceMeshCode: actualMeshCode);
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
