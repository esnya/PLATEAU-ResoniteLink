namespace Plateau.ResoniteLink.Application.Importing;

public sealed class LocalCityGmlDocumentReadResult
{
    internal LocalCityGmlDocumentReadResult(
        LocalCityGmlDocumentSet documentSet,
        LocalCityGmlBootstrapContext bootstrapContext)
    {
        DocumentSet = documentSet;
        BootstrapContext = bootstrapContext;
    }

    public LocalCityGmlDocumentSet DocumentSet { get; }

    internal LocalCityGmlBootstrapContext BootstrapContext { get; }
}
