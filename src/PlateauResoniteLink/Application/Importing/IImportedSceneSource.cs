using System.Collections.Generic;
using System.Threading;

namespace PlateauResoniteLink.Application.Importing;

public interface IImportedSceneSource
{
    ImportedSceneMetadata Metadata { get; }

    IAsyncEnumerable<MaterialBinding> ReadCommonMaterialsAsync(
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ImportedCityObject> ReadCityObjectsAsync(
        CancellationToken cancellationToken = default);
}

