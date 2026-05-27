using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal interface IImportedSceneSourcePreflight
{
    Task ValidateBeforeSinkSetupAsync(CancellationToken cancellationToken = default);
}
