using System.Collections.Concurrent;

using Plateau.ResoniteLink.Cli;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class ResoniteLiveAssetStateStoreTests
{
    [Fact]
    public async Task PersistAsyncRoundTripsAssetFingerprints()
    {
        using TemporaryDirectory directory = new();
        string statePath = Path.Combine(directory.Path, "resonite-live-asset-state.json");
        using ResoniteLiveAssetStateStore store = new();

        await store.PersistAsync(
            statePath,
            new ConcurrentDictionary<string, string>(
                new Dictionary<string, string>
                {
                    ["component-b"] = "fingerprint-b",
                    ["component-a"] = "fingerprint-a",
                },
                StringComparer.Ordinal),
            CancellationToken.None);

        ConcurrentDictionary<string, string> loaded = await store.LoadAsync(statePath, CancellationToken.None);

        Assert.Equal(2, loaded.Count);
        Assert.Equal("fingerprint-a", loaded["component-a"]);
        Assert.Equal("fingerprint-b", loaded["component-b"]);
    }

    [Fact]
    public async Task PersistAsyncCleansTemporaryFileWhenMoveFails()
    {
        using TemporaryDirectory directory = new();
        string statePath = Path.Combine(directory.Path, "resonite-live-asset-state.json");
        Directory.CreateDirectory(statePath);
        using ResoniteLiveAssetStateStore store = new();

        await Assert.ThrowsAnyAsync<IOException>(() =>
            store.PersistAsync(
                statePath,
                new ConcurrentDictionary<string, string>(
                    new Dictionary<string, string>
                    {
                        ["component"] = "fingerprint",
                    },
                    StringComparer.Ordinal),
                CancellationToken.None));

        Assert.DoesNotContain(
            Directory.EnumerateFiles(directory.Path, "resonite-live-asset-state.json.*.tmp"),
            static path => File.Exists(path));
    }
}
