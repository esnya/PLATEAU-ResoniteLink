using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

internal sealed class DryRunResoniteSceneBuilder(Action<string>? progressReporter = null) : IResoniteSceneBuilder
{
    private readonly Dictionary<string, int> packageCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string>? progressReporter = progressReporter;
    private ResoniteConstructionMetadata? metadata;
    private bool beginCalled;
    private bool completed;
    private bool disposed;
    private int cityObjectCount;
    private int heightMapGeometryCount;
    private int materialCount;
    private int meshGeometryCount;

    public Task EnsureConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(PlateauLog.Info("dry-run", "Skipping ResoniteLink connection because --dry-run was requested."));
        return Task.CompletedTask;
    }

    public Task BeginAsync(
        ResoniteConstructionMetadata metadata,
        string workRoot,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);
        cancellationToken.ThrowIfCancellationRequested();

        this.metadata = metadata;
        packageCounts.Clear();
        cityObjectCount = 0;
        meshGeometryCount = 0;
        heightMapGeometryCount = 0;
        materialCount = 0;
        beginCalled = true;
        completed = false;

        ReportProgress(PlateauLog.Info("dry-run", $"Captured scene metadata for '{metadata.WorldName}'."));
        return Task.CompletedTask;
    }

    public Task ProcessCityObjectAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(cityObject);
        cancellationToken.ThrowIfCancellationRequested();

        if (!beginCalled)
        {
            throw new InvalidOperationException("BeginAsync must be called before ProcessCityObjectAsync.");
        }

        if (completed)
        {
            throw new InvalidOperationException("CompleteAsync has already been called.");
        }

        cityObjectCount++;
        materialCount += cityObject.Materials.Count;

        packageCounts.TryGetValue(cityObject.PackageName, out int existingCount);
        packageCounts[cityObject.PackageName] = existingCount + 1;

        switch (cityObject.Geometry)
        {
            case ResoniteTriangleMeshGeometry:
                meshGeometryCount++;
                break;
            case ResoniteHeightMapGridGeometry:
                heightMapGeometryCount++;
                break;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> CompleteAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (!beginCalled || metadata is null)
        {
            throw new InvalidOperationException("BeginAsync must be called before CompleteAsync.");
        }

        if (completed)
        {
            throw new InvalidOperationException("CompleteAsync has already been called.");
        }

        completed = true;

        string packageSummary = packageCounts.Count == 0
            ? "none"
            : string.Join(
                ", ",
                packageCounts
                    .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(static pair => $"{pair.Key}:{pair.Value}"));

        ReportProgress(
            PlateauLog.Info(
                "dry-run",
                $"Validated {cityObjectCount} city objects for '{metadata.WorldName}' without a live Resonite session (mesh={meshGeometryCount}, heightmap={heightMapGeometryCount}, materials={materialCount}, packages={packageSummary})."));

        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public ValueTask DisposeAsync()
    {
        disposed = true;
        return ValueTask.CompletedTask;
    }

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
