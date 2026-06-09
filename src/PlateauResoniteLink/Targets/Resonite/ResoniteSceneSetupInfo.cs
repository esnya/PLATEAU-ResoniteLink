using System.Collections.Generic;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record ResoniteLicenseAttributionMetadata(
    bool RequireCredit,
    string? CreditText,
    string? LicenseName,
    string? LicenseUrl);

internal sealed record ResoniteSceneSetupInfo(
    string Dataset,
    string MeshCode,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string> SelectedMeshCodes,
    IReadOnlyDictionary<string, string> SourceFilePackageNamesByRelativePath,
    ResoniteLicenseAttributionMetadata DatasetLicense);
