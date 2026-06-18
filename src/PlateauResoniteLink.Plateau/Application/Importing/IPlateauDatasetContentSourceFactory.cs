using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Plateau.Application.Importing;

internal interface IPlateauDatasetContentSourceFactory
{
    Task<IPlateauDatasetContentSource> CreateAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);
}
