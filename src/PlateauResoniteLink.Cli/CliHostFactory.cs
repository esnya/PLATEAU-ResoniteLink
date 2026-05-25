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
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Targets.Resonite.Diagnostics;
using PlateauResoniteLink.Transport.ResoniteLink;
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
        services.AddSingleton<IImportServiceFactory, DefaultImportServiceFactory>();
        services.AddSingleton<IPlateauDatasetSourceResolverFactory, DefaultPlateauDatasetSourceResolverFactory>();
        services.AddSingleton<ICanonicalSceneDumpSinkFactory, DefaultCanonicalSceneDumpSinkFactory>();
        services.AddSingleton<ISceneSinkFactory, DefaultSceneSinkFactory>();
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
    PlateauImportService Create(ImportCommandOptions options, Action<string>? progressReporter);
}

internal interface IPlateauDatasetSourceResolverFactory
{
    IPlateauDatasetSourceResolver Create();
}

internal interface ISceneSinkFactory
{
    ISceneSink Create(ImportCommandOptions options, Action<string>? progressReporter);
}

internal interface ICanonicalSceneDumpSinkFactory
{
    ISceneSink Create(
        AsyncServiceScope scope,
        ImportCommandOptions options,
        Action<string>? progressReporter);
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
    public PlateauImportService Create(ImportCommandOptions options, Action<string>? progressReporter)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new PlateauImportService(
            sceneSinkFactory.Create(options, progressReporter),
            datasetSourceResolverFactory.Create(),
            importedSceneSourceFactory,
            commonMaterials,
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

internal sealed class DefaultSceneSinkFactory(
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory serviceScopeFactory)
    : ISceneSinkFactory
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The returned ScopedSceneSink owns the target and associated service scope for the import run.")]
    public ISceneSink Create(ImportCommandOptions options, Action<string>? progressReporter)
    {
        ArgumentNullException.ThrowIfNull(options);

        AsyncServiceScope scope = serviceScopeFactory.CreateAsyncScope();
        try
        {
            if (!string.IsNullOrWhiteSpace(options.CanonicalSceneDumpPath))
            {
                return scope.ServiceProvider
                    .GetRequiredService<ICanonicalSceneDumpSinkFactory>()
                    .Create(scope, options, progressReporter);
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
                progressReporter);
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
}

internal sealed class DefaultCanonicalSceneDumpSinkFactory : ICanonicalSceneDumpSinkFactory
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The returned ScopedSceneSink owns the canonical dump sink, target, recording client, and associated service scope.")]
    public ISceneSink Create(
        AsyncServiceScope scope,
        ImportCommandOptions options,
        Action<string>? progressReporter)
    {
        ArgumentNullException.ThrowIfNull(options);

        SceneSinkRecordingClient recordingClient = new();
        try
        {
            ResoniteLiveSceneImportTarget dumpTarget = CreateCanonicalDumpTarget(
                scope.ServiceProvider,
                recordingClient,
                options,
                progressReporter);
            return new ScopedSceneSink(
                scope,
                new CanonicalSceneDumpSink(dumpTarget, recordingClient, options.CanonicalSceneDumpPath!));
        }
        catch
        {
            recordingClient.Dispose();
            throw;
        }
    }

    private static ResoniteLiveSceneImportTarget CreateCanonicalDumpTarget(
        IServiceProvider serviceProvider,
        SceneSinkRecordingClient recordingClient,
        ImportCommandOptions options,
        Action<string>? progressReporter)
    {
        ResoniteLiveSceneImportTargetOptions targetOptions = new(
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
            progressReporter);

        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.Disabled;
        IResoniteQueuedCityObjectEnqueuer queuedCityObjectEnqueuer = new ResoniteQueuedCityObjectEnqueuer();
        ResoniteLiveSendQueue queue = new(
            queuedCityObjectEnqueuer,
            new ResoniteLiveSendFinalizer(queuedCityObjectEnqueuer));
        ResoniteQueuedCityObjectWorker queuedCityObjectWorker = new(
            new ResoniteQueuedCityObjectLaneProcessor(
                new ResoniteQueuedCityObjectSender(
                    new ResoniteQueuedCityObjectPreparer(
                        new ResoniteQueuedGeometryPreparer(),
                        new ResoniteQueuedTexturePreparer(
                            new DeterministicTerrainTextureAssetGenerator(),
                            serviceProvider.GetRequiredService<IResoniteDatasetLicenseWriter>())),
                    new ResoniteQueuedSendFailurePolicy(),
                    serviceProvider.GetRequiredService<IResonitePreparedCityObjectImporter>())));
        return new ResoniteLiveSceneImportTarget(
            targetOptions,
            new ResoniteLiveSceneImportDependencies(
                new SingleRecordingClientSession(recordingClient),
                diagnostics,
                serviceProvider.GetRequiredService<IResoniteLiveSendStartRequestFactory>(),
                new ResoniteLiveSendRunStarter(
                    serviceProvider.GetRequiredService<IResoniteSceneSetupInterpreter>(),
                    serviceProvider.GetRequiredService<IResoniteLiveSendConnectionInitializer>(),
                    serviceProvider.GetRequiredService<IResoniteCommonMaterialSetupPreparer>(),
                    serviceProvider.GetRequiredService<IResoniteCommonMaterialSetupCachePrimer>(),
                    serviceProvider.GetRequiredService<ILiveSendRunPlanFactory>(),
                    serviceProvider.GetRequiredService<ILiveSendRunStateFactory>(),
                    new ResoniteLiveSendWorkerLauncher(queuedCityObjectWorker),
                    serviceProvider.GetRequiredService<IResoniteSharedSlotIndexFactory>()),
                queue));
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
