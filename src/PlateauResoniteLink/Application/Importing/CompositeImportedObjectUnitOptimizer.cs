using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class CompositeImportedObjectUnitOptimizer(
    IReadOnlyList<IImportedObjectUnitOptimizer> optimizers) : IImportedObjectUnitOptimizer
{
    private readonly IReadOnlyList<IImportedObjectUnitOptimizer> optimizers =
        optimizers ?? throw new ArgumentNullException(nameof(optimizers));

    public IAsyncEnumerable<ImportedObjectUnit> OptimizeAsync(
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objectUnits);

        return optimizers.Aggregate(
            objectUnits,
            (current, optimizer) => optimizer.OptimizeAsync(current, cancellationToken));
    }
}
