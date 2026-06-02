using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public abstract record ValidatedDatasetLocation(DatasetSourceKind SourceKind)
{
    public abstract DatasetLocation ToDatasetLocation();
}

public sealed record ValidatedLocalDatasetLocation : ValidatedDatasetLocation
{
    public ValidatedLocalDatasetLocation(string localSourcePath)
        : base(DatasetSourceKind.Local)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localSourcePath);
        LocalSourcePath = localSourcePath.Trim();
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
        if (!serverUri.IsAbsoluteUri)
        {
            throw new ArgumentException("The remote dataset location URI must be absolute.", nameof(serverUri));
        }

        if (!string.Equals(serverUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(serverUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The remote dataset location URI must use http or https.", nameof(serverUri));
        }

        ServerUri = serverUri;
    }

    public Uri ServerUri { get; }

    public override DatasetLocation ToDatasetLocation() => new RemoteDatasetLocation(ServerUri);
}
