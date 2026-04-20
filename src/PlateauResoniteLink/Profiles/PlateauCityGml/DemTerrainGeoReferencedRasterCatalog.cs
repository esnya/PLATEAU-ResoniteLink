using PlateauResoniteLink.Domain.Importing;
namespace PlateauResoniteLink.Application.Importing;

internal sealed class DemTerrainGeoReferencedRasterCatalog : IDemTerrainGeoReferencedRasterCatalog
{
    private readonly string? directRasterPath;
    private readonly string? singleRelativeRasterPath;
    private readonly IPlateauDatasetContentSource? contentSource;
    private readonly string outputRoot;
    private readonly IReadOnlyList<string> orderedRelativeRasterPaths;
    private readonly IReadOnlyDictionary<string, string> relativeRasterPathsByStem;
    private readonly object cachedRasterSourceTaskGate = new();
    private readonly Dictionary<string, Task<TerrainTextureGeoReferencedRasterSource?>> cachedRasterSourceTasksByCacheKey =
        new(StringComparer.OrdinalIgnoreCase);

    private DemTerrainGeoReferencedRasterCatalog(
        string? directRasterPath,
        string? singleRelativeRasterPath,
        IPlateauDatasetContentSource? contentSource,
        string outputRoot,
        IReadOnlyList<string> orderedRelativeRasterPaths,
        IReadOnlyDictionary<string, string> relativeRasterPathsByStem)
    {
        this.directRasterPath = directRasterPath;
        this.singleRelativeRasterPath = singleRelativeRasterPath;
        this.contentSource = contentSource;
        this.outputRoot = outputRoot;
        this.orderedRelativeRasterPaths = orderedRelativeRasterPaths;
        this.relativeRasterPathsByStem = relativeRasterPathsByStem;
    }

    public static async Task<IDemTerrainGeoReferencedRasterCatalog?> CreateAsync(
        PlateauImportSource? source,
        IPlateauDatasetContentSourceFactory datasetContentSourceFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetContentSourceFactory);

        if (source is not PlateauLocalImportSource localSource
            || string.IsNullOrWhiteSpace(localSource.LocalSourcePath))
        {
            return null;
        }

        string fullSourcePath = Path.GetFullPath(localSource.LocalSourcePath);
        if (File.Exists(fullSourcePath) && IsSupportedRasterFile(fullSourcePath))
        {
            return new DemTerrainGeoReferencedRasterCatalog(
                directRasterPath: fullSourcePath,
                singleRelativeRasterPath: null,
                contentSource: null,
                outputRoot: Path.GetDirectoryName(fullSourcePath) ?? fullSourcePath,
                orderedRelativeRasterPaths: [],
                relativeRasterPathsByStem: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        if (!Directory.Exists(fullSourcePath) && !IsSupportedArchive(fullSourcePath))
        {
            return null;
        }

        IPlateauDatasetContentSource contentSource = await datasetContentSourceFactory.CreateAsync(
            fullSourcePath,
            cancellationToken);
        string[] rasterFiles = contentSource.EnumerateFiles()
            .Where(IsSupportedRasterFile)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (rasterFiles.Length == 0)
        {
            return null;
        }

        Dictionary<string, string> rasterFilesByStem = rasterFiles
            .GroupBy(static path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).First())
            .ToDictionary(
                static path => Path.GetFileNameWithoutExtension(path),
                static path => path,
                StringComparer.OrdinalIgnoreCase);

        return new DemTerrainGeoReferencedRasterCatalog(
            directRasterPath: null,
            singleRelativeRasterPath: rasterFiles.Length == 1 ? rasterFiles[0] : null,
            contentSource,
            outputRoot: Path.GetDirectoryName(fullSourcePath) ?? fullSourcePath,
            orderedRelativeRasterPaths: rasterFiles,
            relativeRasterPathsByStem: rasterFilesByStem);
    }

    public async Task<TerrainTextureGeoReferencedRasterSource?> TryResolveRasterSourceAsync(
        string cacheKey,
        string meshCode,
        GeographicRectangle overlayBounds,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(meshCode);

        Task<TerrainTextureGeoReferencedRasterSource?> resolveTask;
        lock (cachedRasterSourceTaskGate)
        {
            if (!cachedRasterSourceTasksByCacheKey.TryGetValue(cacheKey, out resolveTask!))
            {
                resolveTask = ResolveRasterSourceCoreAsync(meshCode, overlayBounds, CancellationToken.None);
                cachedRasterSourceTasksByCacheKey[cacheKey] = resolveTask;
                _ = resolveTask.ContinueWith(
                    completedTask => RemoveFaultedResolveTask(cacheKey, completedTask),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        try
        {
            return await resolveTask.WaitAsync(cancellationToken);
        }
        catch
        {
            if (!resolveTask.IsCompleted)
            {
                throw;
            }

            lock (cachedRasterSourceTaskGate)
            {
                if (cachedRasterSourceTasksByCacheKey.TryGetValue(cacheKey, out Task<TerrainTextureGeoReferencedRasterSource?>? cachedTask)
                    && ReferenceEquals(cachedTask, resolveTask))
                {
                    cachedRasterSourceTasksByCacheKey.Remove(cacheKey);
                }
            }

            throw;
        }
    }

    private async Task<TerrainTextureGeoReferencedRasterSource?> ResolveRasterSourceCoreAsync(
        string meshCode,
        GeographicRectangle overlayBounds,
        CancellationToken cancellationToken)
    {
        foreach (string rasterPath in await ResolveCandidateRasterPathsAsync(meshCode, cancellationToken))
        {
            GeoReferencedRasterMetadata? metadata = await TerrainTextureGeoReferencedRasterMetadataReader.TryReadMetadataAsync(
                rasterPath,
                cancellationToken);
            if (metadata is null
                || !metadata.IsUsable
                || !Contains(metadata.GeographicBounds, overlayBounds))
            {
                continue;
            }

            return new TerrainTextureGeoReferencedRasterSource(rasterPath, metadata);
        }

        return null;
    }

    private void RemoveFaultedResolveTask(
        string cacheKey,
        Task<TerrainTextureGeoReferencedRasterSource?> completedTask)
    {
        if (!completedTask.IsFaulted && !completedTask.IsCanceled)
        {
            return;
        }

        lock (cachedRasterSourceTaskGate)
        {
            if (cachedRasterSourceTasksByCacheKey.TryGetValue(cacheKey, out Task<TerrainTextureGeoReferencedRasterSource?>? cachedTask)
                && ReferenceEquals(cachedTask, completedTask))
            {
                cachedRasterSourceTasksByCacheKey.Remove(cacheKey);
            }
        }
    }

    private async Task<IReadOnlyList<string>> ResolveCandidateRasterPathsAsync(
        string meshCode,
        CancellationToken cancellationToken)
    {
        if (directRasterPath is not null)
        {
            return [directRasterPath];
        }

        if (contentSource is null)
        {
            return [];
        }

        List<string> relativePaths = [];
        HashSet<string> seenRelativePaths = new(StringComparer.OrdinalIgnoreCase);
        if (relativeRasterPathsByStem.TryGetValue(meshCode, out string? exactMatchPath)
            && seenRelativePaths.Add(exactMatchPath))
        {
            relativePaths.Add(exactMatchPath);
        }

        if (singleRelativeRasterPath is not null
            && seenRelativePaths.Add(singleRelativeRasterPath))
        {
            relativePaths.Add(singleRelativeRasterPath);
        }

        foreach (string relativePath in orderedRelativeRasterPaths)
        {
            if (seenRelativePaths.Add(relativePath))
            {
                relativePaths.Add(relativePath);
            }
        }

        List<string> localPaths = [];
        foreach (string relativePath in relativePaths)
        {
            localPaths.Add(await contentSource.EnsureLocalFileAsync(relativePath, outputRoot, cancellationToken));
        }

        return localPaths;
    }

    private static bool IsSupportedArchive(string path)
    {
        string extension = Path.GetExtension(path);
        return string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedRasterFile(string path)
    {
        string extension = Path.GetExtension(path);
        return string.Equals(extension, ".tif", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".tiff", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(GeographicRectangle outer, GeographicRectangle inner)
    {
        return outer.MinLatitude <= inner.MinLatitude + CoverageToleranceDegrees
            && outer.MaxLatitude + CoverageToleranceDegrees >= inner.MaxLatitude
            && outer.MinLongitude <= inner.MinLongitude + CoverageToleranceDegrees
            && outer.MaxLongitude + CoverageToleranceDegrees >= inner.MaxLongitude;
    }

    private const double CoverageToleranceDegrees = 1e-6;
}
