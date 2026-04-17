using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed record SceneBuildRequest(
    PlateauImportRequest Request,
    IPlateauDatasetContentSource DatasetContentSource,
    IReadOnlyList<ResoniteMaterialBinding> CommonMaterials,
    string WorkRoot,
    IReadOnlyList<string> PackageNames,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string> RequestedMeshCodes,
    ResoniteLicenseComponentMetadata DatasetLicense);
