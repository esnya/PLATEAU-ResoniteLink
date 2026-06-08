using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Diagnostics;
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

        services.AddImportedSceneSourceServices();
        services.AddResoniteLiveSendTargetServices();

        services.AddSingleton<DatasetInspectionService>();
        services.AddSingleton<Func<IPlateauDatasetSourceResolver>>(_ =>
            () => new CkanPlateauDatasetSourceResolver(
                _.GetRequiredService<IHttpClientFactory>().CreateClient(CliHostFactory.PlateauDatasetResolverHttpClientName),
                _.GetRequiredService<IRemoteArchiveDistributionPolicy>(),
                _.GetRequiredService<IArchiveFileLayoutPolicy>()));
        services.AddSingleton<Func<ImportCommandOptions, ILoggerFactory, ISceneSink>>(_ =>
        {
            IHttpClientFactory httpClientFactory = _.GetRequiredService<IHttpClientFactory>();
            IServiceScopeFactory serviceScopeFactory = _.GetRequiredService<IServiceScopeFactory>();

            return (options, loggerFactory) => CliServiceCollectionExtensions.CreateSceneSink(
                httpClientFactory,
                serviceScopeFactory,
                options,
                loggerFactory);
        });
        services.AddSingleton<Func<ImportCommandOptions, ILoggerFactory, PlateauImportService>>(_ =>
        {
            Func<IPlateauDatasetSourceResolver> createDatasetSourceResolver =
                _.GetRequiredService<Func<IPlateauDatasetSourceResolver>>();
            Func<ImportCommandOptions, ILoggerFactory, ISceneSink> createSceneSink =
                _.GetRequiredService<Func<ImportCommandOptions, ILoggerFactory, ISceneSink>>();
            IImportedSceneSourceFactory importedSceneSourceFactory =
                _.GetRequiredService<IImportedSceneSourceFactory>();
            CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials =
                _.GetRequiredService<CommonMaterialCatalog<DefaultCommonMaterialMember>>();
            IArchiveFileLayoutPolicy archiveFileLayoutPolicy =
                _.GetRequiredService<IArchiveFileLayoutPolicy>();

            return (options, loggerFactory) => CliServiceCollectionExtensions.CreateImportService(
                createDatasetSourceResolver,
                createSceneSink,
                importedSceneSourceFactory,
                commonMaterials,
                archiveFileLayoutPolicy,
                options,
                loggerFactory);
        });
        services.AddSingleton<CliApplication>(_ => new CliApplication(
            standardOutput,
            standardError,
            _.GetRequiredService<Func<ImportCommandOptions, ILoggerFactory, PlateauImportService>>(),
            _.GetRequiredService<DatasetInspectionService>()));

        return services;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "PlateauImportService owns the target lifetime and disposes it after each execution.")]
    internal static PlateauImportService CreateImportService(
        Func<IPlateauDatasetSourceResolver> createDatasetSourceResolver,
        Func<ImportCommandOptions, ILoggerFactory, ISceneSink> createSceneSink,
        IImportedSceneSourceFactory importedSceneSourceFactory,
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
        IArchiveFileLayoutPolicy archiveFileLayoutPolicy,
        ImportCommandOptions options,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        return new PlateauImportService(
            createSceneSink(options, loggerFactory),
            createDatasetSourceResolver(),
            importedSceneSourceFactory,
            commonMaterials,
            archiveFileLayoutPolicy,
            loggerFactory);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The returned ScopedSceneSink owns the target and associated service scope for the import run.")]
    internal static ISceneSink CreateSceneSink(
        IHttpClientFactory httpClientFactory,
        IServiceScopeFactory serviceScopeFactory,
        ImportCommandOptions options,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(serviceScopeFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        AsyncServiceScope scope = serviceScopeFactory.CreateAsyncScope();
        try
        {
            if (!string.IsNullOrWhiteSpace(options.CanonicalSceneDumpPath))
            {
                IResoniteLiveSceneImportFactory dumpTargetFactory =
                    scope.ServiceProvider.GetRequiredService<IResoniteLiveSceneImportFactory>();
                return new ScopedSceneSink(
                    scope,
                    CanonicalSceneDumpSink.Create(
                        dumpTargetFactory,
                        CreateCanonicalDumpTargetOptions(options, loggerFactory),
                        options.CanonicalSceneDumpPath));
            }

            ResoniteLiveSceneImportTargetOptions targetOptions = new(
                options.ResoniteLinkUri!,
                options.ResoniteLinkConnectionCount,
                options.EnableSendMetrics,
                options.MemoryProfile switch
                {
                    PlateauImportMemoryProfile.Small => ResoniteImportMemoryProfile.Small,
                    PlateauImportMemoryProfile.Large => ResoniteImportMemoryProfile.Large,
                    _ => throw new ArgumentOutOfRangeException(nameof(options), options.MemoryProfile, "Unsupported memory profile."),
                },
                options.EnableMeshBake,
                options.TerrainTileCacheRoot,
                options.DisableTerrainTileCache,
                loggerFactory);
            IResoniteLiveSceneImportFactory targetFactory =
                scope.ServiceProvider.GetRequiredService<IResoniteLiveSceneImportFactory>();
            ResoniteLiveSceneImportTarget target = targetFactory.CreateTarget(
                targetOptions,
                httpClientFactory.CreateClient(CliHostFactory.TerrainTextureAssetsHttpClientName));
            return new ScopedSceneSink(scope, target);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private static ResoniteLiveSceneImportTargetOptions CreateCanonicalDumpTargetOptions(
        ImportCommandOptions options,
        ILoggerFactory loggerFactory)
    {
        return new ResoniteLiveSceneImportTargetOptions(
            new Uri("ws://localhost:1/"),
            ConnectionCount: 1,
            EnableSendMetrics: false,
            options.MemoryProfile switch
            {
                PlateauImportMemoryProfile.Small => ResoniteImportMemoryProfile.Small,
                PlateauImportMemoryProfile.Large => ResoniteImportMemoryProfile.Large,
                _ => throw new ArgumentOutOfRangeException(nameof(options), options.MemoryProfile, "Unsupported memory profile."),
            },
            options.EnableMeshBake,
            TerrainTileCacheRoot: null,
            DisableTerrainTileCache: true,
            loggerFactory);
    }
}

internal sealed class ScopedSceneSink(
    AsyncServiceScope scope,
    ISceneSink inner) : ISceneSink
{
    public Task<SceneImportExecutionResult> ExecuteAsync(
        SceneImportExecutionPlan plan,
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        CancellationToken cancellationToken = default)
    {
        return inner.ExecuteAsync(plan, objectUnits, cancellationToken);
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
