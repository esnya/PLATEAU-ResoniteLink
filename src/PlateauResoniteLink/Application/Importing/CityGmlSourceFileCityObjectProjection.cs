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
        CityGmlAppearanceStore appearanceStore,
        CoordinateReferenceSystem coordinateReferenceSystem,
        LodFilteringStrategy lodFilteringStrategy,
        SelectCityGmlLod selectLod)
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
            coordinateReferenceSystem,
            sourceFile.RequiresMeshCodeBoundsFilter ? requestedMeshAreas : [],
            lodFilteringStrategy,
            selectLod);
    }
}
