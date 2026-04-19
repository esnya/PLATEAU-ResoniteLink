namespace Plateau.ResoniteLink.Domain.Importing;

public abstract record PlateauImportSource(DatasetSourceKind SourceKind)
{
    public static PlateauImportSource FromInput(string sourceInput)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceInput);

        string trimmedInput = sourceInput.Trim();
        if (trimmedInput.Contains("://", StringComparison.Ordinal)
            && Uri.TryCreate(trimmedInput, UriKind.Absolute, out Uri? uri))
        {
            return Remote(uri);
        }

        return Local(trimmedInput);
    }

    public static PlateauImportSource Local(string? localSourcePath)
    {
        return new PlateauLocalImportSource(localSourcePath);
    }

    public static PlateauImportSource Remote(Uri? serverUri)
    {
        return new PlateauRemoteImportSource(serverUri);
    }

    public static PlateauImportSource FromLegacy(
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

public sealed record PlateauLocalImportSource(string? LocalSourcePath)
    : PlateauImportSource(DatasetSourceKind.Local);

public sealed record PlateauRemoteImportSource(Uri? ServerUri)
    : PlateauImportSource(DatasetSourceKind.Remote);
