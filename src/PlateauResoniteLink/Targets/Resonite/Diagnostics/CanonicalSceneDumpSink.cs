using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Diagnostics;

internal sealed class CanonicalSceneDumpSink(
    ResoniteLiveSceneImportTarget inner,
    SceneSinkRecordingClient recordingClient,
    string outputPath) : ISceneSink
{
    public async Task<SceneImportExecutionResult> ExecuteAsync(
        SceneImportExecutionPlan plan,
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        CancellationToken cancellationToken = default)
    {
        SceneImportExecutionResult result = await inner.ExecuteAsync(plan, objectUnits, cancellationToken);
        string canonicalJson = SceneSinkRecordingClientCanonicalDump.CreateCanonicalJson(recordingClient);
        await WriteAtomicallyAsync(outputPath, canonicalJson, cancellationToken);
        return result;
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
    public Task<GeneratedTerrainTexture> EnsureTextureAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] rawBytes =
        [
            128, 160, 192, 255,
            128, 160, 192, 255,
            128, 160, 192, 255,
            128, 160, 192, 255,
        ];
        TerrainTextureSource? usedSource = terrainTextureOverlay.GetRequiredPrimaryTileSource();
        return Task.FromResult(new GeneratedTerrainTexture(
            new ResoniteRawTextureImport(
                2,
                2,
                ResoniteTextureColorProfiles.Srgb,
                rawBytes),
            new ResoniteFloat2(1.0, 1.0),
            new ResoniteFloat2(0.0, 0.0),
            usedSource));
    }
}
