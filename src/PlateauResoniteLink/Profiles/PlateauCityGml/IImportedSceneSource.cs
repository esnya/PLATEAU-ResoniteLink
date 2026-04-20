namespace PlateauResoniteLink.Application.Importing;

public interface IImportedSceneSource
{
    ImportedSceneMetadata Metadata { get; }

    [Obsolete(
        "ReadCommonMaterialsAsync is obsolete. Runtime common-material setup uses package catalog instead of source enumeration.")]
    IAsyncEnumerable<MaterialBinding> ReadCommonMaterialsAsync(
        CancellationToken cancellationToken = default);

    IEnumerable<ImportedCityObject> ReadCityObjects();

    IAsyncEnumerable<ImportedCityObject> ReadCityObjectsAsync(
        CancellationToken cancellationToken = default);
}
