using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Core.Application.Importing;

public interface IImportedSceneSourcePreflight
{
    Task ValidateBeforeSinkSetupAsync(CancellationToken cancellationToken = default);
}
