namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record PlateauImportRequest(
    string Dataset,
    string MeshCode,
    DatasetSourceKind SourceKind,
    string? InputPath,
    Uri? ServerUri);
