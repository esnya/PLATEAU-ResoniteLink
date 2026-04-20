using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public abstract record ValidatedPlateauImportSource(DatasetSourceKind SourceKind);

public sealed record ValidatedPlateauLocalImportSource(string LocalSourcePath)
    : ValidatedPlateauImportSource(DatasetSourceKind.Local);

public sealed record ValidatedPlateauRemoteImportSource(Uri ServerUri)
    : ValidatedPlateauImportSource(DatasetSourceKind.Remote);
