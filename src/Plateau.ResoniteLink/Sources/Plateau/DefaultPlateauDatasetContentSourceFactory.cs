namespace Plateau.ResoniteLink.Application.Importing;

public sealed class DefaultPlateauDatasetContentSourceFactory(
    IRemoteArchiveDistributionPolicy remoteArchiveDistributionPolicy,
    IArchiveFileLayoutPolicy archiveFileLayoutPolicy) : IPlateauDatasetContentSourceFactory
{
    public Task<IPlateauDatasetContentSource> CreateAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        return PlateauDatasetContentSourceFactory.CreateAsync(
            sourcePath,
            remoteArchiveDistributionPolicy,
            archiveFileLayoutPolicy,
            cancellationToken);
    }
}
