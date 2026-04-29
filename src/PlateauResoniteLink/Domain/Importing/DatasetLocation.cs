using System;

namespace PlateauResoniteLink.Domain.Importing;

public abstract record DatasetLocation(DatasetSourceKind SourceKind)
{
    public static DatasetLocation Local(string? localSourcePath)
    {
        return new LocalDatasetLocation(localSourcePath);
    }

    public static DatasetLocation Remote(Uri? serverUri)
    {
        return new RemoteDatasetLocation(serverUri);
    }

}

public sealed record LocalDatasetLocation(string? LocalSourcePath)
    : DatasetLocation(DatasetSourceKind.Local);

public sealed record RemoteDatasetLocation(Uri? ServerUri)
    : DatasetLocation(DatasetSourceKind.Remote);
