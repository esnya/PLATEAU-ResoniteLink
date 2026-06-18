using System.Collections.Generic;
using System.Threading;

namespace PlateauResoniteLink.Core.Application.Importing.Contracts;

public interface IImportedSceneSource
{
    ImportedSceneMetadata Metadata { get; }

    IAsyncEnumerable<ImportedObjectUnit> ReadObjectUnitsAsync(
        CancellationToken cancellationToken = default);
}
