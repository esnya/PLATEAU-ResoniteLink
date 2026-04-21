using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public abstract record ValidatedDatasetLocation(DatasetSourceKind SourceKind);

public sealed record ValidatedLocalDatasetLocation(string LocalSourcePath)
    : ValidatedDatasetLocation(DatasetSourceKind.Local);

public sealed record ValidatedRemoteDatasetLocation(Uri ServerUri)
    : ValidatedDatasetLocation(DatasetSourceKind.Remote);
