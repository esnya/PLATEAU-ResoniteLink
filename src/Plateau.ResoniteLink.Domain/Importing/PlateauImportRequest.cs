namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record PlateauImportRequest(
    string Dataset,
    string MeshCode,
    DatasetSourceKind SourceKind,
    string? LocalSourcePath,
    Uri? ServerUri,
    IReadOnlyList<string>? PackageNames = null);
