using System;
using System.Collections.Generic;
using System.Threading;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class CompositeImportedCityObjectOptimizer(
    params IImportedCityObjectOptimizer[] optimizers) : IImportedCityObjectOptimizer
{
    private readonly IImportedCityObjectOptimizer[] optimizers = optimizers
        ?? throw new ArgumentNullException(nameof(optimizers));

    public IAsyncEnumerable<ImportedCityObject> OptimizeAsync(
        IAsyncEnumerable<ImportedCityObject> cityObjects,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObjects);

        IAsyncEnumerable<ImportedCityObject> current = cityObjects;
        foreach (IImportedCityObjectOptimizer optimizer in optimizers)
        {
            current = optimizer.OptimizeAsync(current, cancellationToken);
        }

        return current;
    }
}
