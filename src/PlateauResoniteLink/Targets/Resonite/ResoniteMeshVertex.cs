using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

public sealed record ResoniteMeshVertex(
    ResoniteFloat3 Position,
    ResoniteFloat3 Normal,
    ResoniteFloat2 UV0,
    ResoniteColor? Color = null);
