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

public sealed record ValidatedLocalDatasetLocation(string LocalSourcePath) : ValidatedDatasetLocation(DatasetSourceKind.Local)
{
    public string LocalSourcePath { get; } = string.IsNullOrWhiteSpace(LocalSourcePath)
        ? throw new ArgumentException("The local source path must not be empty.", nameof(LocalSourcePath))
        : LocalSourcePath;

    public override DatasetLocation ToDatasetLocation() => new LocalDatasetLocation(LocalSourcePath);
}

public sealed record ValidatedRemoteDatasetLocation(Uri ServerUri) : ValidatedDatasetLocation(DatasetSourceKind.Remote)
{
    public Uri ServerUri { get; } = ValidateServerUri(ServerUri);

    public override DatasetLocation ToDatasetLocation() => new RemoteDatasetLocation(ServerUri);

    private static Uri ValidateServerUri(Uri serverUri)
    {
        ArgumentNullException.ThrowIfNull(serverUri);
        if (!serverUri.IsAbsoluteUri)
        {
            throw new ArgumentException("The remote dataset location URI must be absolute.", nameof(serverUri));
        }

        if (!string.Equals(serverUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(serverUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The remote dataset location URI must use http or https.", nameof(serverUri));
        }

        return serverUri;
    }
}
