using System.Collections.Generic;
using System.Threading;

namespace PlateauResoniteLink.Application.Importing;

public interface IImportedSceneSource
{
    ImportedSceneMetadata Metadata { get; }

    IAsyncEnumerable<ImportedObjectUnit> ReadObjectUnitsAsync(
        CancellationToken cancellationToken = default);
}
