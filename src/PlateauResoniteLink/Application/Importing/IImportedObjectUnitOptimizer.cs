using System.Collections.Generic;
using System.Threading;

namespace PlateauResoniteLink.Application.Importing;

internal interface IImportedObjectUnitOptimizer
{
    IAsyncEnumerable<ImportedObjectUnit> OptimizeAsync(
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        CancellationToken cancellationToken = default);
}
