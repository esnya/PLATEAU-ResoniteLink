using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

public interface IImportedSceneSource
{
    ImportedSceneMetadata Metadata { get; }

    Task ValidateBeforeSinkSetupAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    IAsyncEnumerable<ImportedObjectUnit> ReadObjectUnitsAsync(
        CancellationToken cancellationToken = default);
}
