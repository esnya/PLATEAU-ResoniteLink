using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PlateauResoniteLink.Resonite.Transport.ResoniteLink;
using PlateauResoniteLink.Core.Application.Importing;
using PlateauResoniteLink.Core.Application.Importing.Contracts;

using PlateauResoniteLink.Core;

namespace PlateauResoniteLink.Resonite.Targets.Resonite.Diagnostics;

public static class ResoniteCanonicalSceneDumpServiceCollectionExtensions
{
    public static IServiceCollection AddResoniteCanonicalSceneDumpServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddResoniteTargetPipelineServices();
        services.TryAddScoped<IResoniteRecordingLiveSceneImportFactory, ResoniteRecordingLiveSceneImportFactory>();
        services.TryAddScoped<IResoniteCanonicalSceneDumpSinkFactory, ResoniteCanonicalSceneDumpSinkFactory>();

        return services;
    }
}

public interface IResoniteCanonicalSceneDumpSinkFactory
{
    ISceneSink Create(
        ResoniteLiveSceneImportTargetOptions options,
        string outputPath,
        HttpClient terrainTextureAssetHttpClient);
}

internal sealed class ResoniteCanonicalSceneDumpSinkFactory(
    IResoniteRecordingLiveSceneImportFactory targetFactory,
    ITerrainTextureAssetGeneratorFactory terrainTextureAssetGeneratorFactory) : IResoniteCanonicalSceneDumpSinkFactory
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "CanonicalSceneDumpSink owns the recording client after successful construction; failures dispose it here.")]
    public ISceneSink Create(
        ResoniteLiveSceneImportTargetOptions options,
        string outputPath,
        HttpClient terrainTextureAssetHttpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);

        SceneSinkRecordingClient recordingClient = new();
        try
        {
            ITerrainTextureAssetGenerator terrainTextureAssetGenerator =
                terrainTextureAssetGeneratorFactory.Create(
                    terrainTextureAssetHttpClient,
                    new TerrainTextureAssetGeneratorOptions(
                        options.TerrainTileCacheRoot,
                        options.DisableTerrainTileCache));
            ResoniteLiveSceneImportTarget target = targetFactory.CreateTarget(
                options,
                new SingleRecordingClientSession(recordingClient),
                ResoniteLinkSendDiagnostics.Disabled,
                terrainTextureAssetGenerator);
            return new CanonicalSceneDumpSink(target, recordingClient, outputPath);
        }
        catch
        {
            recordingClient.Dispose();
            throw;
        }
    }
}

internal sealed class CanonicalSceneDumpSink(
    ISceneSink inner,
    SceneSinkRecordingClient recordingClient,
    string outputPath) : ISceneSink
{
    public async Task<SceneImportExecutionResult> ExecuteAsync(
        SceneImportExecutionPlan plan,
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        CancellationToken cancellationToken = default)
    {
        List<ImportedObjectUnit> recordedObjectUnits = [];
        SceneImportExecutionResult result = await inner.ExecuteAsync(
            plan,
            RecordObjectUnits(objectUnits, recordedObjectUnits, cancellationToken),
            cancellationToken);
        string canonicalJson = SceneSinkRecordingClientCanonicalDump.CreateCanonicalJson(recordingClient, recordedObjectUnits);
        await WriteAtomicallyAsync(outputPath, canonicalJson, cancellationToken);
        return result;
    }

    private static async IAsyncEnumerable<ImportedObjectUnit> RecordObjectUnits(
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        List<ImportedObjectUnit> recordedObjectUnits,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (ImportedObjectUnit objectUnit in objectUnits.WithCancellation(cancellationToken))
        {
            recordedObjectUnits.Add(objectUnit);
            yield return objectUnit;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await inner.DisposeAsync();
        }
        finally
        {
            recordingClient.Dispose();
        }
    }

    private static async Task WriteAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = fullPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, Encoding.UTF8, cancellationToken);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }
}

internal sealed class SingleRecordingClientSession(SceneSinkRecordingClient client) : ILiveSendClientSession
{
    public ResoniteLinkSendDiagnostics Diagnostics { get; } = ResoniteLinkSendDiagnostics.Disabled;

    public Task EnsureConnectedAsync(
        LiveSendConnectionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public IResoniteLinkClient GetRequiredClient()
    {
        return client;
    }

    public ValueTask ResetClientsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public void DisposeClients()
    {
    }
}
