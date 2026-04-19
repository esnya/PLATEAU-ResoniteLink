namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record ResoniteAttribution(
    LicenseAttributionMetadata DatasetLicense,
    IReadOnlyList<ResoniteMaterialAttribution> MaterialLicenses);
