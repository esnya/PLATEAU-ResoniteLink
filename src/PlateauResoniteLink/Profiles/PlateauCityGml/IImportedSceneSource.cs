namespace PlateauResoniteLink.Application.Importing;

public interface IImportedSceneSource
{
    ImportedSceneMetadata Metadata { get; }

    IAsyncEnumerable<MaterialBinding> ReadCommonMaterialsAsync(
        CancellationToken cancellationToken = default);

    IEnumerable<ImportedCityObject> ReadCityObjects();

    IAsyncEnumerable<ImportedCityObject> ReadCityObjectsAsync(
        CancellationToken cancellationToken = default);
}
