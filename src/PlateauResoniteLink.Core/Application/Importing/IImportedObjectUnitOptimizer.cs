using System.Collections.Generic;
using System.Threading;

using PlateauResoniteLink.Core.Application.Importing.Contracts;

namespace PlateauResoniteLink.Core.Application.Importing;

public interface IImportedObjectUnitOptimizer
{
    IAsyncEnumerable<ImportedObjectUnit> OptimizeAsync(
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        CancellationToken cancellationToken = default);
}
