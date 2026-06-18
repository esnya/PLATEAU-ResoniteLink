using System.Collections.Generic;
using System.Threading;

namespace PlateauResoniteLink.Application.Importing.Contracts;

public interface IImportedSceneSource
{
    ImportedSceneMetadata Metadata { get; }

    IAsyncEnumerable<ImportedObjectUnit> ReadObjectUnitsAsync(
        CancellationToken cancellationToken = default);
}
