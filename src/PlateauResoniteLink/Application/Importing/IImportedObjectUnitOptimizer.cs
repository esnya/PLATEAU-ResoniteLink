using System.Collections.Generic;
using System.Threading;

using PlateauResoniteLink.Application.Importing.Contracts;

namespace PlateauResoniteLink.Application.Importing;

public interface IImportedObjectUnitOptimizer
{
    IAsyncEnumerable<ImportedObjectUnit> OptimizeAsync(
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        CancellationToken cancellationToken = default);
}
