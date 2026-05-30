using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public abstract record ValidatedDatasetLocation(DatasetSourceKind SourceKind)
{
    public abstract DatasetLocation ToDatasetLocation();
}

public sealed record ValidatedLocalDatasetLocation(string LocalSourcePath)
    : ValidatedDatasetLocation(DatasetSourceKind.Local)
{
    public override DatasetLocation ToDatasetLocation() => new LocalDatasetLocation(LocalSourcePath);
}

public sealed record ValidatedRemoteDatasetLocation(Uri ServerUri)
    : ValidatedDatasetLocation(DatasetSourceKind.Remote)
{
    public override DatasetLocation ToDatasetLocation() => new RemoteDatasetLocation(ServerUri);
}
