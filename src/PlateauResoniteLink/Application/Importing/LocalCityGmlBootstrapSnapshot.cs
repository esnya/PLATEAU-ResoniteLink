namespace PlateauResoniteLink.Application.Importing;

internal sealed class LocalCityGmlBootstrapSnapshot
{
    internal LocalCityGmlBootstrapSnapshot(
        LocalCityGmlDocumentSet documentSet,
        LocalCityGmlBootstrapContext bootstrapContext)
    {
        DocumentSet = documentSet;
        BootstrapContext = bootstrapContext;
    }

    public LocalCityGmlDocumentSet DocumentSet { get; }

    internal LocalCityGmlBootstrapContext BootstrapContext { get; }
}
