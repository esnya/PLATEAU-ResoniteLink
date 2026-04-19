using System.Xml.Linq;

namespace Plateau.ResoniteLink.Application.Importing;

internal interface ICityGmlAppearanceStore
{
    void LoadFromDocument(XDocument document);

    void ApplyAppearanceElement(XElement appearanceElement);

    CityGmlResolvedAppearance Resolve(string polygonId);
}
