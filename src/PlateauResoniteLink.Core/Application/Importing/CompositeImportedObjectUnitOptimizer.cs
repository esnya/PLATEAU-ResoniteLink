using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using PlateauResoniteLink.Core.Application.Importing.Contracts;

namespace PlateauResoniteLink.Core.Application.Importing;

public sealed class CompositeImportedObjectUnitOptimizer(
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
