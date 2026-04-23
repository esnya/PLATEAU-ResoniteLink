using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class PassthroughImportedCityObjectOptimizer : IImportedCityObjectOptimizer
{
    public async IAsyncEnumerable<ImportedCityObject> OptimizeAsync(
        IAsyncEnumerable<ImportedCityObject> cityObjects,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (ImportedCityObject cityObject in cityObjects.WithCancellation(cancellationToken))
        {
            yield return cityObject;
        }
    }
}
