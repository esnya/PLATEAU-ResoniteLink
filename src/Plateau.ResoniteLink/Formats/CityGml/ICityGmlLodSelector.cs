using System.Xml.Linq;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal interface ICityGmlLodSelector
{
    CityGmlLodSelection SelectPreferredSurfaceElements(
        XElement cityObjectElement,
        string packageName,
        bool isMarking,
        LodFilteringStrategy lodFilteringStrategy);
}
