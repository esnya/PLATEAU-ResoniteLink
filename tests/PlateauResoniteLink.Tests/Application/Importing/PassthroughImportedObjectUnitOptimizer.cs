using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Importing.Contracts;

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;


namespace PlateauResoniteLink.Tests.Application.Importing;

internal sealed class PassthroughImportedObjectUnitOptimizer : IImportedObjectUnitOptimizer
{
    public async IAsyncEnumerable<ImportedObjectUnit> OptimizeAsync(
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using IAsyncEnumerator<ImportedObjectUnit> enumerator = objectUnits.GetAsyncEnumerator(cancellationToken);
        while (await enumerator.MoveNextAsync())
        {
            yield return enumerator.Current;
        }
    }
}
