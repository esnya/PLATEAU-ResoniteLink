using System.Collections.Generic;
using System.Threading;

namespace PlateauResoniteLink.Application.Importing;

internal interface IImportedCityObjectOptimizer
{
    IAsyncEnumerable<ImportedCityObject> OptimizeAsync(
        IAsyncEnumerable<ImportedCityObject> cityObjects,
        CancellationToken cancellationToken = default);
}
