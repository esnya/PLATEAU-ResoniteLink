using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public abstract record ValidatedDatasetLocation
{
    private protected ValidatedDatasetLocation(DatasetSourceKind sourceKind)
    {
        SourceKind = sourceKind;
    }

    public DatasetSourceKind SourceKind { get; }

    public abstract DatasetLocation ToDatasetLocation();
}

public sealed record ValidatedLocalDatasetLocation : ValidatedDatasetLocation
{
    public ValidatedLocalDatasetLocation(string localSourcePath)
        : base(DatasetSourceKind.Local)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localSourcePath);
        LocalSourcePath = localSourcePath;
    }

    public string LocalSourcePath { get; }

    public override DatasetLocation ToDatasetLocation() => new LocalDatasetLocation(LocalSourcePath);
}

public sealed record ValidatedRemoteDatasetLocation : ValidatedDatasetLocation
{
    public ValidatedRemoteDatasetLocation(Uri serverUri)
        : base(DatasetSourceKind.Remote)
    {
        ArgumentNullException.ThrowIfNull(serverUri);
        ServerUri = serverUri;
    }

    public Uri ServerUri { get; }

    public override DatasetLocation ToDatasetLocation() => new RemoteDatasetLocation(ServerUri);
}
