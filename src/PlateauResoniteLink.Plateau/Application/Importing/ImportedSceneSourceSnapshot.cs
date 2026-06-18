namespace PlateauResoniteLink.Application.Importing;

internal sealed class ImportedSceneSourceSnapshot
{
    internal ImportedSceneSourceSnapshot(
        ImportedSceneSourceDataset documentSet,
        ImportedSceneSourceContext discoveryContext)
    {
        DocumentSet = documentSet;
        DiscoveryContext = discoveryContext;
    }

    public ImportedSceneSourceDataset DocumentSet { get; }

    internal ImportedSceneSourceContext DiscoveryContext { get; }
}
