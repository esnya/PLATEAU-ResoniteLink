using Microsoft.Extensions.DependencyInjection;

using Plateau.ResoniteLink.Application.Importing;

namespace Plateau.ResoniteLink.Tests.UseCases;

public sealed class PlateauImportServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPlateauCityGmlImportServicesResolvesDefaultContentSourceDependencies()
    {
        ServiceProvider provider = new ServiceCollection()
            .AddPlateauCityGmlImportServices()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IArchiveFileLayoutPolicy>());
        Assert.NotNull(provider.GetRequiredService<IRemoteArchiveDistributionPolicy>());
        Assert.NotNull(provider.GetRequiredService<IPlateauDatasetContentSourceFactory>());
        Assert.NotNull(provider.GetRequiredService<ICityGmlAppearanceStoreFactory>());
        Assert.NotNull(provider.GetRequiredService<ICityGmlLodSelector>());
        Assert.NotNull(provider.GetRequiredService<IDefaultMaterialResolver>());
        Assert.NotNull(provider.GetRequiredService<IResoniteConstructionComposer>());
        Assert.NotNull(provider.GetRequiredService<ICityGmlDocumentReader>());
        Assert.NotNull(provider.GetRequiredService<IResoniteConstructionSourceFactory>());
    }

    [Fact]
    public void AddPlateauCityGmlImportServicesPreservesCustomDatasetContentSourceFactory()
    {
        CustomPlateauDatasetContentSourceFactory factory = new();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IPlateauDatasetContentSourceFactory>(factory)
            .AddPlateauCityGmlImportServices()
            .BuildServiceProvider();

        Assert.Same(factory, provider.GetRequiredService<IPlateauDatasetContentSourceFactory>());
    }

    private sealed class CustomPlateauDatasetContentSourceFactory : IPlateauDatasetContentSourceFactory
    {
        public Task<IPlateauDatasetContentSource> CreateAsync(
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
