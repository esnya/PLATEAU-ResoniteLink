using System;

namespace PlateauResoniteLink.Domain.Importing;

public abstract record DatasetLocation(DatasetSourceKind SourceKind)
{
    public static DatasetLocation Local(string localSourcePath)
    {
        return new LocalDatasetLocation(localSourcePath);
    }

    public static DatasetLocation Remote(Uri serverUri)
    {
        return new RemoteDatasetLocation(serverUri);
    }

}

public sealed record LocalDatasetLocation : DatasetLocation
{
    public LocalDatasetLocation(string localSourcePath)
        : base(DatasetSourceKind.Local)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localSourcePath);
        LocalSourcePath = localSourcePath;
    }

    public string LocalSourcePath { get; }
}

public sealed record RemoteDatasetLocation : DatasetLocation
{
    public RemoteDatasetLocation(Uri serverUri)
        : base(DatasetSourceKind.Remote)
    {
        ArgumentNullException.ThrowIfNull(serverUri);
        ServerUri = serverUri;
    }

    public Uri ServerUri { get; }
}
