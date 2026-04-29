using System.Xml.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal interface ICityGmlSourceRepresentationSelector
{
    CityGmlSourceRepresentationSelection[] SelectSurfaceRepresentations(
        XElement cityObjectElement,
        string packageName,
        bool isMarking,
        LodFilteringStrategy lodFilteringStrategy);
}
