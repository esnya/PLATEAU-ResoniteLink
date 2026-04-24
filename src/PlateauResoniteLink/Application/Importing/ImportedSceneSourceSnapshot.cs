namespace PlateauResoniteLink.Application.Importing;

internal sealed class ImportedSceneSourceSnapshot
{
    internal ImportedSceneSourceSnapshot(
        ImportedSceneSourceDataset documentSet,
        ImportedSceneSourceContext bootstrapContext)
    {
        DocumentSet = documentSet;
        BootstrapContext = bootstrapContext;
    }

    public ImportedSceneSourceDataset DocumentSet { get; }

    internal ImportedSceneSourceContext BootstrapContext { get; }
}
