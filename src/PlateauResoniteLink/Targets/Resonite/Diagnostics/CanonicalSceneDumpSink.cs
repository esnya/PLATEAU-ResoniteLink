using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;
using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Importing.Contracts;

namespace PlateauResoniteLink.Targets.Resonite.Diagnostics;

internal interface IResoniteCanonicalSceneDumpSinkFactory
{
    ISceneSink Create(
        ResoniteLiveSceneImportTargetOptions options,
        string outputPath);
}

internal sealed class ResoniteCanonicalSceneDumpSinkFactory(
    IResoniteLiveSceneImportFactory targetFactory) : IResoniteCanonicalSceneDumpSinkFactory
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "CanonicalSceneDumpSink owns the recording client after successful construction; failures dispose it here.")]
    public ISceneSink Create(
        ResoniteLiveSceneImportTargetOptions options,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        SceneSinkRecordingClient recordingClient = new();
        try
        {
            ResoniteLiveSceneImportTarget target = targetFactory.CreateTarget(
                options,
                new SingleRecordingClientSession(recordingClient),
                ResoniteLinkSendDiagnostics.Disabled,
                new DeterministicTerrainTextureAssetGenerator());
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

internal sealed class DeterministicTerrainTextureAssetGenerator : ITerrainTextureAssetGenerator
{
    private static readonly byte[] RawTextureBytes =
    [
        128, 160, 192, 255,
        128, 160, 192, 255,
        128, 160, 192, 255,
        128, 160, 192, 255,
    ];

    public Task<GeneratedTerrainTexture> EnsureTextureAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TerrainTextureSource usedSource = terrainTextureOverlay.GetRequiredPrimaryTileSource();
        TerrainTextureSourceUsage usage = TerrainTextureSourceUsage.FromSource(usedSource);
        return Task.FromResult(new GeneratedTerrainTexture(
            TextureImportSourceFactory.CreateInMemoryRaw(
                2,
                2,
                ResoniteTextureColorProfiles.Srgb,
                RawTextureBytes,
                "canonical-dump-terrain-texture"),
            new ResoniteFloat2(1.0, 1.0),
            new ResoniteFloat2(0.0, 0.0),
            usage));
    }
}
