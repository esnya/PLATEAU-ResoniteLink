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

    public static DatasetLocation FromLegacy(
        DatasetSourceKind sourceKind,
        string? localSourcePath,
        Uri? serverUri)
    {
        return sourceKind switch
        {
            DatasetSourceKind.Local => Local(localSourcePath),
            DatasetSourceKind.Remote => Remote(serverUri),
            _ => throw new InvalidOperationException($"Unsupported dataset source kind '{sourceKind}'."),
        };
    }
}

public sealed record LocalDatasetLocation(string? LocalSourcePath)
    : DatasetLocation(DatasetSourceKind.Local);

public sealed record RemoteDatasetLocation(Uri? ServerUri)
    : DatasetLocation(DatasetSourceKind.Remote);
