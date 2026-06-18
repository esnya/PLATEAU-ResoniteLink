using System.Xml.Linq;

using PlateauResoniteLink.Core.Domain.Importing;

namespace PlateauResoniteLink.Plateau.Application.Importing.CityGml;

internal interface ICityGmlLodSelector
{
    CityGmlLodSelection SelectPreferredSurfaceElements(
        XElement cityObjectElement,
        string packageName,
        bool isMarking,
        LodFilteringStrategy lodFilteringStrategy);
}
