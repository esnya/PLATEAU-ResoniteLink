using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public abstract record ValidatedPlateauImportSource(DatasetSourceKind SourceKind);

public sealed record ValidatedPlateauLocalImportSource(string LocalSourcePath)
    : ValidatedPlateauImportSource(DatasetSourceKind.Local);

public sealed record ValidatedPlateauRemoteImportSource(Uri ServerUri)
    : ValidatedPlateauImportSource(DatasetSourceKind.Remote);
