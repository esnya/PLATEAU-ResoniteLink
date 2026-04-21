using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

public sealed record ResoniteAttribution(
    LicenseAttributionMetadata DatasetLicense,
    IReadOnlyList<ResoniteMaterialAttribution> MaterialLicenses);
