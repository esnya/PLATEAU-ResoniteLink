using System.Xml.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing.CityGml;

internal interface ICityGmlLodSelector
{
    CityGmlLodSelection SelectPreferredSurfaceElements(
        XElement cityObjectElement,
        string packageName,
        bool isMarking,
        LodFilteringStrategy lodFilteringStrategy);
}
