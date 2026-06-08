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
            coordinateReferenceSystem,
            sourceFile.RequiresMeshCodeBoundsFilter ? requestedMeshAreas : [],
            lodFilteringStrategy);
    }
}
