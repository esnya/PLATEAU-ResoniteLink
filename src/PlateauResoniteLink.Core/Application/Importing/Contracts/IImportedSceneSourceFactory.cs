using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing.Contracts;

public interface IImportedSceneSourceFactory
{
    Task<IImportedSceneSource> CreateAsync(
        ResolvedLocalPlateauImportRequest request,
        CancellationToken cancellationToken = default);
}
