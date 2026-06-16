using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityGmlParsedCityObjectReader
{
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
        CityGmlParsedCityObjectReaderContext context = CityGmlParsedCityObjectReaderContext.Create(
            cityObjectElement,
            packageName,
            relativeSourceFile,
            actualMeshCode,
            sharedAcrossMeshCodes,
            lodSelector,
            lodFilteringStrategy);

        BuildingAttributeContext buildingAttributes = BuildingAttributeParser.Parse(cityObjectElement);
        int? floorsAboveGround = BuildingAttributeQueries.TryGetKnownPositiveInteger(buildingAttributes.StoreysAboveGround);
        double? measuredHeightMeters = BuildingAttributeQueries.TryGetKnownPositiveMetric(buildingAttributes.MeasuredHeightMeters);

        if (!context.ShouldInclude)
        {
            return null;
        }

        ParsedSurface[] surfaces = ParseSurfaces(context.PreferredSurfaceElements, packageName, appearanceStore);

        if (surfaces.Length == 0)
        {
            return null;
        }

        if (!CityGmlMeshCodeBoundsFilter.IntersectsRequestedMeshCodeBounds(
                context.ResolvedActualMeshCode,
                sharedAcrossMeshCodes,
                coordinateReferenceSystem,
                requestedMeshCodeBounds,
                surfaces))
        {
            return null;
        }

        return new ParsedCityObject(
            context.SlotKey,
            context.DisplayName,
            context.PackageName,
            context.ResolvedActualMeshCode,
            context.LodLevel,
            surfaces,
            coordinateReferenceSystem,
            context.RelativeSourceFile,
            SharedAcrossMeshCodes: sharedAcrossMeshCodes,
            FloorsAboveGround: floorsAboveGround,
            MeasuredHeightMeters: measuredHeightMeters,
            BuildingAttributes: buildingAttributes,
            SourceMeshCode: actualMeshCode);
    }

    private static ParsedSurface[] ParseSurfaces(
        IEnumerable<XElement> surfaceElements,
        string packageName,
        ICityGmlAppearanceStore appearanceStore)
    {
        List<ParsedSurface> surfaces = [];
        foreach (XElement surfaceElement in surfaceElements)
        {
            if (CityGmlParsedSurfaceReader.TryParse(surfaceElement, appearanceStore) is not { } surface)
            {
                continue;
            }

            surfaces.Add(CityGmlParsedSurfaceReader.ApplyPackageDefaults(packageName, surface));
        }

        return surfaces
            .OrderBy(static surface => surface, ParsedSurfaceStructuralComparer.Instance)
            .ToArray();
    }
}
