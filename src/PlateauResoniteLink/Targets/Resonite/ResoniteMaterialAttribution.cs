using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

public sealed record ResoniteMaterialAttribution(
    string MaterialKey,
    LicenseAttributionMetadata? License);
