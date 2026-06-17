using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Application.Importing.Plateau;
using PlateauResoniteLink.Application.Importing.Source;

namespace PlateauResoniteLink.Application.Importing.CityGml;

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

        return CityGmlParsedCityObjectReader.Parse(
            cityObjectElement,
            sourceFile.PackageName,
            sourceFile.RelativePath,
            sourceFile.MatchedMeshCode,
            sourceFile.RequiresMeshCodeBoundsFilter,
            appearanceStore,
            lodSelector,
            coordinateReferenceSystem,
            sourceFile.RequiresMeshCodeBoundsFilter ? requestedMeshAreas : [],
            lodFilteringStrategy);
    }
}
