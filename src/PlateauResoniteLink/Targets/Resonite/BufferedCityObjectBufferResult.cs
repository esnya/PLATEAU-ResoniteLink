using System.Collections.Generic;

namespace PlateauResoniteLink.Targets.Resonite;

internal readonly record struct BufferedCityObjectBufferResult(
    bool Buffered,
    IReadOnlyList<ResoniteConstructionCityObject> ReadyCityObjects);
