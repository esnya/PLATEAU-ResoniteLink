using System.Collections.Generic;

namespace PlateauResoniteLink.Domain.Importing;

public sealed record SceneBootstrapInfo(
    string Dataset,
    string MeshCode,
    string LocalSourcePath,
    IReadOnlyList<string> PackageNames,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string> SelectedMeshCodes,
    LicenseAttributionMetadata DatasetLicense,
    IReadOnlyList<LicenseAttributionMetadata> AdditionalDatasetLicenses);
