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
        ProjectionCoordinateReferenceSystem coordinateReferenceSystem,
        LodFilteringStrategy lodFilteringStrategy)
    {
        XElement? cityObjectElement = cityObjectMember.Elements().FirstOrDefault();
        if (cityObjectElement is null)
        {
            return null;
        }

        return LocalCityGmlObjectProjection.ParseCityObject(
            cityObjectElement,
            sourceFile.PackageName,
            sourceFile.RelativePath,
            sourceFile.MatchedMeshCode,
            sourceFile.RequiresMeshAreaFilter,
            appearanceStore,
            lodSelector,
            coordinateReferenceSystem,
            sourceFile.RequiresMeshAreaFilter ? requestedMeshAreas : null,
            lodFilteringStrategy);
    }
}
