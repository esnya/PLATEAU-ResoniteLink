using System.Xml.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal interface ICityGmlAppearanceStore
{
    void LoadFromDocument(XDocument document);

    void ApplyAppearanceElement(XElement appearanceElement);

    CityGmlResolvedAppearance Resolve(string polygonId);

    bool HasGeoreferencedTexture(string polygonId);
}
