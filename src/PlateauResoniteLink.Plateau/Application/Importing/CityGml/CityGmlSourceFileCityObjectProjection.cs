using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

using PlateauResoniteLink.Core.Domain.Importing;
using PlateauResoniteLink.Plateau.Application.Importing.Plateau;
using PlateauResoniteLink.Plateau.Application.Importing.Source;

namespace PlateauResoniteLink.Plateau.Application.Importing.CityGml;

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
