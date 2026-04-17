namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record ResoniteAttribution(
    ResoniteLicenseComponentMetadata DatasetLicense,
    IReadOnlyList<ResoniteMaterialAttribution> MaterialLicenses);
