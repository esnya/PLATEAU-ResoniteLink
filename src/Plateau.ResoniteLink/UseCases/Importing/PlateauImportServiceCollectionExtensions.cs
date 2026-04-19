using Microsoft.Extensions.DependencyInjection;

using Plateau.ResoniteLink.Profiles.PlateauCityGml;

namespace Plateau.ResoniteLink.Application.Importing;

public static class PlateauImportServiceCollectionExtensions
{
    public static IServiceCollection AddPlateauCityGmlImportServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ICityGmlAppearanceStoreFactory, CityGmlAppearanceStoreFactory>();
        services.AddSingleton<ICityGmlLodSelector, CityGmlLodSelector>();
        services.AddSingleton<ICityGmlDocumentReader>(provider =>
            PlateauCityGmlImportComposition.CreateDocumentReader(
                provider.GetRequiredService<ICityGmlAppearanceStoreFactory>(),
                provider.GetRequiredService<ICityGmlLodSelector>()));
        services.AddSingleton<IDefaultMaterialResolver>(_ => PlateauCityGmlImportComposition.CreateMaterialResolver());
        services.AddSingleton<ICityGmlGeometryProjector>(provider =>
            PlateauCityGmlImportComposition.CreateGeometryProjector(
                provider.GetRequiredService<IDefaultMaterialResolver>()));
        services.AddSingleton<IResoniteConstructionComposer>(provider =>
            PlateauCityGmlImportComposition.CreateConstructionComposer(
                provider.GetRequiredService<ICityGmlGeometryProjector>()));
        services.AddSingleton<IResoniteConstructionSourceFactory>(provider =>
            PlateauCityGmlImportComposition.CreateConstructionSourceFactory(
                provider.GetRequiredService<ICityGmlDocumentReader>(),
                provider.GetRequiredService<IResoniteConstructionComposer>()));

        return services;
    }
}
