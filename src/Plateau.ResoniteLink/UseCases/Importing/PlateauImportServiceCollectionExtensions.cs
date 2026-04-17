using Microsoft.Extensions.DependencyInjection;

namespace Plateau.ResoniteLink.Application.Importing;

public static class PlateauImportServiceCollectionExtensions
{
    public static IServiceCollection AddPlateauCityGmlImportServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ICityGmlDocumentReader, LocalCityGmlDocumentReader>();
        services.AddSingleton<IDefaultMaterialResolver>(_ => new DefaultMaterialResolver());
        services.AddSingleton<ICityGmlGeometryProjector>(provider =>
            new LocalCityGmlGeometryProjector(provider.GetRequiredService<IDefaultMaterialResolver>()));
        services.AddSingleton<IResoniteConstructionComposer>(provider =>
            new LocalCityGmlConstructionComposer(provider.GetRequiredService<ICityGmlGeometryProjector>()));
        services.AddSingleton<IResoniteConstructionSourceFactory>(provider =>
            new LocalCityGmlConstructionSourceFactory(
                provider.GetRequiredService<ICityGmlDocumentReader>(),
                provider.GetRequiredService<IResoniteConstructionComposer>()));

        return services;
    }
}
