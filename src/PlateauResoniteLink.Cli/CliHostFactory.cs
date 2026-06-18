using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Importing.Contracts;
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
        HostApplicationBuilder builder = new(new HostApplicationBuilderSettings
        {
            Args = args,
            DisableDefaults = true,
        });
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

        services.AddSingleton(new CliConsoleWriters(standardOutput, standardError));
        services.AddSingleton<ICliRootCommandFactory, CliCommandFactory>();
        services.AddSingleton<ICliCommandProvider, ImportCliCommand>();
        services.AddSingleton<ICliCommandProvider, SearchCliCommand>();
        services.AddSingleton<ICliCommandProvider, StatsCliCommand>();
        services.AddSingleton<IImportCommandHandler, DefaultImportCommandHandler>();
        services.AddSingleton<ISearchCommandHandler, DefaultSearchCommandHandler>();
        services.AddSingleton<IStatsCommandHandler, DefaultStatsCommandHandler>();
        services.AddSingleton<IImportServiceFactory, DefaultImportServiceFactory>();
        services.AddSingleton<IPlateauDatasetSourceResolverFactory, DefaultPlateauDatasetSourceResolverFactory>();
        services.AddSingleton<ISceneSinkFactory, DefaultSceneSinkFactory>();
        services.AddSingleton<CliApplication>();

        return services;
    }
}

internal interface IImportServiceFactory
{
    PlateauImportService Create(
        ImportSinkCliOptions sinkOptions,
        ResoniteSceneBuildCliOptions sceneBuildOptions);
}

internal interface IPlateauDatasetSourceResolverFactory
{
    IPlateauDatasetSourceResolver Create();
}

internal interface ISceneSinkFactory
{
    ISceneSink Create(
        ImportSinkCliOptions sinkOptions,
        ResoniteSceneBuildCliOptions sceneBuildOptions);
}

internal sealed class DefaultImportServiceFactory(
    IPlateauDatasetSourceResolverFactory datasetSourceResolverFactory,
    ISceneSinkFactory sceneSinkFactory,
    IImportedSceneSourceFactory importedSceneSourceFactory,
    CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
    IArchiveFileLayoutPolicy archiveFileLayoutPolicy) : IImportServiceFactory
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "PlateauImportService owns the target lifetime and disposes it after each execution.")]
    public PlateauImportService Create(
        ImportSinkCliOptions sinkOptions,
        ResoniteSceneBuildCliOptions sceneBuildOptions)
    {
        ArgumentNullException.ThrowIfNull(sinkOptions);
        ArgumentNullException.ThrowIfNull(sceneBuildOptions);

        return new PlateauImportService(
            sceneSinkFactory.Create(sinkOptions, sceneBuildOptions),
            datasetSourceResolverFactory.Create(),
            importedSceneSourceFactory,
            commonMaterials,
            archiveFileLayoutPolicy);
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

internal sealed class DefaultSceneSinkFactory(
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory serviceScopeFactory)
    : ISceneSinkFactory
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The returned ScopedSceneSink owns the target and associated service scope for the import run.")]
    public ISceneSink Create(
        ImportSinkCliOptions sinkOptions,
        ResoniteSceneBuildCliOptions sceneBuildOptions)
    {
        ArgumentNullException.ThrowIfNull(sinkOptions);
        ArgumentNullException.ThrowIfNull(sceneBuildOptions);

        AsyncServiceScope scope = serviceScopeFactory.CreateAsyncScope();
        try
        {
            if (sinkOptions is CanonicalSceneDumpSinkCliOptions canonicalDump)
            {
                IResoniteCanonicalSceneDumpSinkFactory dumpSinkFactory =
                    scope.ServiceProvider.GetRequiredService<IResoniteCanonicalSceneDumpSinkFactory>();
                return new ScopedSceneSink(
                    scope,
                    dumpSinkFactory.Create(
                        CreateCanonicalDumpTargetOptions(sceneBuildOptions),
                        canonicalDump.OutputPath));
            }

            if (sinkOptions is not LiveResoniteSinkCliOptions live)
            {
                throw new InvalidOperationException($"Unsupported import sink options '{sinkOptions.GetType().Name}'.");
            }

            ResoniteLiveSceneImportTargetOptions liveTargetOptions = new(
                live.Transport.Endpoint,
                live.Transport.ConnectionCount,
                live.Transport.EnableSendMetrics,
                sceneBuildOptions.MemoryProfile switch
                {
                    PlateauImportMemoryProfile.Small => ResoniteImportMemoryProfile.Small,
                    PlateauImportMemoryProfile.Large => ResoniteImportMemoryProfile.Large,
                    _ => throw new ArgumentOutOfRangeException(nameof(sceneBuildOptions), sceneBuildOptions.MemoryProfile, "Unsupported memory profile."),
                },
                sceneBuildOptions.EnableMeshBake,
                live.TerrainTileCache.TerrainTileCacheRoot,
                live.TerrainTileCache.DisableTerrainTileCache,
                sceneBuildOptions.EnableDistanceCulling);
            IResoniteLiveSceneImportFactory targetFactory =
                scope.ServiceProvider.GetRequiredService<IResoniteLiveSceneImportFactory>();
            ResoniteLiveSceneImportTarget target = targetFactory.CreateTarget(
                liveTargetOptions,
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
        ResoniteSceneBuildCliOptions options)
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
            options.EnableDistanceCulling);
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
