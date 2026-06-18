using PlateauResoniteLink.Core.Application.Importing.Contracts;

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;


namespace PlateauResoniteLink.Tests;

internal static class ImportedSceneSourceTestExtensions
{
    internal static async IAsyncEnumerable<ImportedCityObject> ReadCityObjectsAsync(
        this IImportedSceneSource source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (ImportedObjectUnit objectUnit in source.ReadObjectUnitsAsync(cancellationToken))
        {
            foreach (ImportedCityObject cityObject in objectUnit.CityObjects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return cityObject;
            }
        }
    }

    internal static async Task<List<ImportedCityObject>> ToCityObjectListAsync(
        this IImportedSceneSource source,
        CancellationToken cancellationToken = default)
    {
        List<ImportedCityObject> cityObjects = [];
        await foreach (ImportedCityObject cityObject in source.ReadCityObjectsAsync(cancellationToken))
        {
            cityObjects.Add(cityObject);
        }

        return cityObjects;
    }
}
