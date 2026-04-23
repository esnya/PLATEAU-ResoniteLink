using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class ImportedDynamicMaterialUvUnitOptimizer : IImportedObjectUnitOptimizer
{
    public async IAsyncEnumerable<ImportedObjectUnit> OptimizeAsync(
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using IAsyncEnumerator<ImportedObjectUnit> enumerator = objectUnits.GetAsyncEnumerator(cancellationToken);
        while (await enumerator.MoveNextAsync())
        {
            ImportedObjectUnit objectUnit = enumerator.Current;
            yield return objectUnit with
            {
                CityObjects = objectUnit.CityObjects
                    .Select(ImportedDynamicMaterialUvNormalizer.Normalize)
                    .ToArray(),
            };
        }
    }
}
