using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class CountingImportedObjectUnitStream(
    IAsyncEnumerable<ImportedObjectUnit> objectUnits,
    Action<int> onReadAdditionalCityObjects)
{
    private readonly IAsyncEnumerable<ImportedObjectUnit> objectUnits =
        objectUnits ?? throw new ArgumentNullException(nameof(objectUnits));
    private readonly Action<int> onReadAdditionalCityObjects =
        onReadAdditionalCityObjects ?? throw new ArgumentNullException(nameof(onReadAdditionalCityObjects));

    public async IAsyncEnumerable<ImportedObjectUnit> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (ImportedObjectUnit objectUnit in objectUnits.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            onReadAdditionalCityObjects(objectUnit.CityObjects.Count);
            yield return objectUnit;
        }
    }
}
