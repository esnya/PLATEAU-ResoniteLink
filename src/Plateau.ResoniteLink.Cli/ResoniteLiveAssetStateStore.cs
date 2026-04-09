using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plateau.ResoniteLink.Cli;

internal interface IResoniteLiveAssetStateStore : IDisposable
{
    Task PersistAsync(
        string? statePath,
        ConcurrentDictionary<string, string>? assetSourceFingerprints,
        CancellationToken cancellationToken);

    Task<ConcurrentDictionary<string, string>> LoadAsync(
        string statePath,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteLiveAssetStateStore(Action<string>? progressReporter = null) : IResoniteLiveAssetStateStore
{
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly Action<string>? progressReporter = progressReporter;

    public async Task PersistAsync(
        string? statePath,
        ConcurrentDictionary<string, string>? assetSourceFingerprints,
        CancellationToken cancellationToken)
    {
        if (statePath is null || assetSourceFingerprints is null)
        {
            return;
        }

        await writeLock.WaitAsync(cancellationToken);
        try
        {
            string directory = Path.GetDirectoryName(statePath) ?? ".";
            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(
                directory,
                $"{Path.GetFileName(statePath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                KeyValuePair<string, string>[] fingerprintSnapshot = assetSourceFingerprints.ToArray();
                LiveAssetState state = new(
                    fingerprintSnapshot.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                        .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal));
                string json = JsonSerializer.Serialize(state, LiveAssetStateJsonContext.Default.LiveAssetState);
                await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
                File.Move(temporaryPath, statePath, overwrite: true);
            }
            finally
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task<ConcurrentDictionary<string, string>> LoadAsync(
        string statePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(statePath))
        {
            return new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            await using FileStream stream = File.OpenRead(statePath);
            LiveAssetState? state = await JsonSerializer.DeserializeAsync(
                stream,
                LiveAssetStateJsonContext.Default.LiveAssetState,
                cancellationToken);
            return new ConcurrentDictionary<string, string>(
                state?.AssetSourceFingerprints ?? [],
                StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            progressReporter?.Invoke($"[live] Ignoring unreadable asset state cache '{statePath}'.");
            return new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        }
        catch (IOException)
        {
            progressReporter?.Invoke($"[live] Ignoring unavailable asset state cache '{statePath}'.");
            return new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        writeLock.Dispose();
    }
}

internal sealed record LiveAssetState(
    Dictionary<string, string> AssetSourceFingerprints);

[JsonSerializable(typeof(LiveAssetState))]
internal sealed partial class LiveAssetStateJsonContext : JsonSerializerContext
{
}
