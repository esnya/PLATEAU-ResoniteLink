using System.Collections.Generic;
using System.Threading;

namespace PlateauResoniteLink.Application.Importing;

internal delegate IAsyncEnumerable<ImportedObjectUnit> ImportedObjectUnitOptimizer(
    IAsyncEnumerable<ImportedObjectUnit> objectUnits,
    CancellationToken cancellationToken = default);
