using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class CountingImportedObjectUnitStream(
    IAsyncEnumerable<ImportedObjectUnit> source,
    Action<int> onReadAdditionalCityObjects)
{
    public async IAsyncEnumerable<ImportedObjectUnit> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (ImportedObjectUnit objectUnit in source.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            onReadAdditionalCityObjects(objectUnit.CityObjects.Count);
            yield return objectUnit;
        }
    }
}
