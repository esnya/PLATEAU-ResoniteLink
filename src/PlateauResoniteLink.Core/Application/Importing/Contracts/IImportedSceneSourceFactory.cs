using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Core.Application.Importing.Contracts;

public interface IImportedSceneSourceFactory
{
    Task<IImportedSceneSource> CreateAsync(
        ResolvedLocalPlateauImportRequest request,
        CancellationToken cancellationToken = default);
}
