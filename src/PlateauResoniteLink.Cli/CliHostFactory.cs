using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Cli;

internal static class CliHostFactory
{
    internal const string PlateauDatasetResolverHttpClientName = "PlateauDatasetResolver";
    internal const string TerrainTextureAssetsHttpClientName = "TerrainTextureAssets";

    public static IHost Create(string[]? args = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Services.AddCliServices(Console.Out, Console.Error);
        return builder.Build();
    }
}

internal static class CliServiceCollectionExtensions
{
    public static IServiceCollection AddCliServices(
        this IServiceCollection services,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        services.AddHttpClient(CliHostFactory.PlateauDatasetResolverHttpClientName);
        services.AddHttpClient(CliHostFactory.TerrainTextureAssetsHttpClientName);

        services.AddPlateauCityGmlImportServices();
        services.AddResoniteLiveSendTargetServices();

        services.AddSingleton<DatasetInspectionService>();
        services.AddSingleton<IImportServiceFactory, DefaultImportServiceFactory>();
        services.AddSingleton<IPlateauDatasetSourceResolverFactory, DefaultPlateauDatasetSourceResolverFactory>();
        services.AddSingleton<ISceneImportTargetFactory, DefaultSceneImportTargetFactory>();
        services.AddSingleton<CliApplication>(_ => new CliApplication(
            standardOutput,
            standardError,
            _.GetRequiredService<IImportServiceFactory>(),
            _.GetRequiredService<DatasetInspectionService>()));

        return services;
    }
}

internal interface IImportServiceFactory
{
    PlateauImportService Create(BuildCommandOptions options, Action<string>? progressReporter);
}

internal interface IPlateauDatasetSourceResolverFactory
{
    IPlateauDatasetSourceResolver Create();
}

internal interface ISceneImportTargetFactory
{
    ISceneImportTarget Create(BuildCommandOptions options, Action<string>? progressReporter);
}

internal sealed class DefaultImportServiceFactory(
    IPlateauDatasetSourceResolverFactory datasetSourceResolverFactory,
    ISceneImportTargetFactory sceneImportTargetFactory,
    ICityGmlDocumentReader documentReader,
    IImportedSceneSourceFactory constructionSourceFactory,
    IArchiveFileLayoutPolicy archiveFileLayoutPolicy) : IImportServiceFactory
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "PlateauImportService owns the target lifetime and disposes it after each execution.")]
    public PlateauImportService Create(BuildCommandOptions options, Action<string>? progressReporter)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new PlateauImportService(
            sceneImportTargetFactory.Create(options, progressReporter),
            datasetSourceResolverFactory.Create(),
            documentReader,
            constructionSourceFactory,
            archiveFileLayoutPolicy,
            progressReporter);
    }
}

internal sealed class DefaultPlateauDatasetSourceResolverFactory(
    IHttpClientFactory httpClientFactory,
    IRemoteArchiveDistributionPolicy remoteArchiveDistributionPolicy,
    IArchiveFileLayoutPolicy archiveFileLayoutPolicy)
    : IPlateauDatasetSourceResolverFactory
{
    public IPlateauDatasetSourceResolver Create()
    {
        return new CkanPlateauDatasetSourceResolver(
            httpClientFactory.CreateClient(CliHostFactory.PlateauDatasetResolverHttpClientName),
            remoteArchiveDistributionPolicy,
            archiveFileLayoutPolicy);
    }
}

internal sealed class DefaultSceneImportTargetFactory(
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory serviceScopeFactory)
    : ISceneImportTargetFactory
{
    public ISceneImportTarget Create(BuildCommandOptions options, Action<string>? progressReporter)
    {
        ArgumentNullException.ThrowIfNull(options);

        AsyncServiceScope scope = serviceScopeFactory.CreateAsyncScope();
        try
        {
            ResoniteLiveSceneImportTargetOptions targetOptions = new(
                options.ResoniteLinkUri!,
                options.ResoniteLinkConnectionCount,
                options.EnableSendMetrics,
                options.MemoryProfile,
                options.EnableMeshBake,
                options.TerrainTileCacheRoot,
                options.DisableTerrainTileCache,
                progressReporter);
            IResoniteLiveSceneImportFactory targetFactory =
                scope.ServiceProvider.GetRequiredService<IResoniteLiveSceneImportFactory>();
            ResoniteLiveSceneImportTarget target = targetFactory.CreateTarget(
                targetOptions,
                httpClientFactory.CreateClient(CliHostFactory.TerrainTextureAssetsHttpClientName));
            return new ScopedSceneImportTarget(scope, target);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}

internal sealed class ScopedSceneImportTarget(
    AsyncServiceScope scope,
    ISceneImportTarget inner) : ISceneImportTarget
{
    public Task<SceneImportExecutionResult> ExecuteAsync(
        SceneImportExecutionPlan plan,
        IAsyncEnumerable<ImportedCityObject> cityObjects,
        CancellationToken cancellationToken = default)
    {
        return inner.ExecuteAsync(plan, cityObjects, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await inner.DisposeAsync();
        }
        finally
        {
            await scope.DisposeAsync();
        }
    }
}
