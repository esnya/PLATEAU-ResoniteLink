using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed class LocalCityGmlDocumentReader : ICityGmlDocumentReader
{
    private readonly ICityGmlAppearanceStoreFactory appearanceStoreFactory;
    private readonly ICityGmlLodSelector lodSelector;

    public LocalCityGmlDocumentReader()
        : this(new CityGmlAppearanceStoreFactory(), new CityGmlLodSelector())
    {
    }

    internal LocalCityGmlDocumentReader(
        ICityGmlAppearanceStoreFactory appearanceStoreFactory,
        ICityGmlLodSelector lodSelector)
    {
        this.appearanceStoreFactory = appearanceStoreFactory;
        this.lodSelector = lodSelector;
    }

    public async Task<LocalCityGmlDocumentSet> ReadAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return await LocalCityGmlBootstrapPipeline.ReadAsync(
            request,
            appearanceStoreFactory,
            lodSelector,
            progressReporter,
            cancellationToken);
    }
}
