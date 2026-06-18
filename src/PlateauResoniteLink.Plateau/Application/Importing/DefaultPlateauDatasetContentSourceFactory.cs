using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Core.Application.Importing;

namespace PlateauResoniteLink.Plateau.Application.Importing;

internal sealed class DefaultPlateauDatasetContentSourceFactory(
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
