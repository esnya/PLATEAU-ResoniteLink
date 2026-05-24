using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityGmlSourceFileCityObjectProjection
{
    internal static ParsedCityObject? Parse(
        XElement cityObjectMember,
        SourceFileDescriptor sourceFile,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
        ICityGmlAppearanceStore appearanceStore,
        ICityGmlLodSelector lodSelector,
        CoordinateReferenceSystem coordinateReferenceSystem,
        LodFilteringStrategy lodFilteringStrategy)
    {
        XElement? cityObjectElement = cityObjectMember.Elements().FirstOrDefault();
        if (cityObjectElement is null)
        {
            return null;
        }

        LocalCityGmlObjectProjection.ParsedCityObject? cityObject = LocalCityGmlObjectProjection.ParseCityObject(
            cityObjectElement,
            sourceFile.PackageName,
            sourceFile.RelativePath,
            sourceFile.MatchedMeshCode,
            sourceFile.RequiresMeshCodeBoundsFilter,
            appearanceStore,
            lodSelector,
            coordinateReferenceSystem.ToProjectionModel(),
            sourceFile.RequiresMeshCodeBoundsFilter ? requestedMeshAreas : null,
            lodFilteringStrategy);

        return cityObject is null ? null : ParsedCityObject.FromProjectionModel(cityObject);
    }
}
