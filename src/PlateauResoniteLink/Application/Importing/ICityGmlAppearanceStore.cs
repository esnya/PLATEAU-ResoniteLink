using System.Collections.Generic;
using System.Xml.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal interface ICityGmlAppearanceStore
{
    void LoadFromDocument(XDocument document);

    void ApplyAppearanceElement(XElement appearanceElement);

    CityGmlResolvedAppearance Resolve(string polygonId);

    IReadOnlyList<Float2>? ResolveRingUvs(string polygonId, string ringId, int vertexCount);
}
