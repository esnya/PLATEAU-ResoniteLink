namespace PlateauResoniteLink.Domain.Importing;

public sealed record ResoniteMaterialAttribution(
    string MaterialKey,
    LicenseAttributionMetadata? License);
