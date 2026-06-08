using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Application.Importing;

internal static class PassthroughImportedObjectUnitOptimizer
{
    public static async IAsyncEnumerable<ImportedObjectUnit> OptimizeAsync(
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
