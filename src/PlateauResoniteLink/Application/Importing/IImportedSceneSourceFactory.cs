using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace PlateauResoniteLink.Application.Importing;

public interface IImportedSceneSourceFactory
{
    Task<IImportedSceneSource> CreateAsync(
        ResolvedLocalPlateauImportRequest request,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default);
}
