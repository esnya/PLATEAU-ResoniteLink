using System.Collections.Generic;

namespace PlateauResoniteLink.Domain.Importing;

public sealed record ResoniteAttribution(
    LicenseAttributionMetadata DatasetLicense,
    IReadOnlyList<ResoniteMaterialAttribution> MaterialLicenses);
