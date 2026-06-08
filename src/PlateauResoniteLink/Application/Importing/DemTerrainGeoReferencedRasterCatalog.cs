using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class DemTerrainGeoReferencedRasterCatalog : IDemTerrainGeoReferencedRasterCatalog
{
    private readonly string? directRasterPath;
    private readonly string? singleRelativeRasterPath;
    private readonly IPlateauDatasetContentSource? contentSource;
    private readonly string? outputRoot;
    private readonly IReadOnlyList<string> orderedRelativeRasterPaths;
    private readonly IReadOnlyDictionary<string, string> relativeRasterPathsByStem;
    private readonly object cachedRasterSourceTaskGate = new();
    private readonly Dictionary<DemTerrainRasterCacheKey, Task<TerrainTextureGeoReferencedRasterSource?>> cachedRasterSourceTasksByCacheKey =
        [];
    private readonly object cachedLocalRasterFileTaskGate = new();
    private readonly Dictionary<string, Task<string>> cachedLocalRasterFileTasksByRelativePath =
        new(StringComparer.OrdinalIgnoreCase);

    private DemTerrainGeoReferencedRasterCatalog(
        string? directRasterPath,
        string? singleRelativeRasterPath,
        IPlateauDatasetContentSource? contentSource,
        string sourceScopePath,
        string? outputRoot,
        IReadOnlyList<string> orderedRelativeRasterPaths,
        IReadOnlyDictionary<string, string> relativeRasterPathsByStem)
    {
        this.directRasterPath = directRasterPath;
        this.singleRelativeRasterPath = singleRelativeRasterPath;
        this.contentSource = contentSource;
        this.outputRoot = outputRoot;
        this.orderedRelativeRasterPaths = orderedRelativeRasterPaths;
        this.relativeRasterPathsByStem = relativeRasterPathsByStem;
        CacheScope = new DemTerrainRasterSourceScope(sourceScopePath);
    }

    public DemTerrainRasterSourceScope CacheScope { get; }

    public static async Task<IDemTerrainGeoReferencedRasterCatalog?> CreateAsync(
        DatasetLocation? source,
        Func<string, CancellationToken, Task<IPlateauDatasetContentSource>> createDatasetContentSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(createDatasetContentSource);

        if (source is not LocalDatasetLocation localSource)
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
                sourceScopePath: fullSourcePath,
                outputRoot: null,
                orderedRelativeRasterPaths: [],
                relativeRasterPathsByStem: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        if (!Directory.Exists(fullSourcePath) && !IsSupportedArchive(fullSourcePath))
        {
            return null;
        }

        IPlateauDatasetContentSource contentSource = await createDatasetContentSource(
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
            sourceScopePath: fullSourcePath,
            outputRoot: Path.GetDirectoryName(fullSourcePath) ?? fullSourcePath,
            orderedRelativeRasterPaths: rasterFiles,
            relativeRasterPathsByStem: rasterFilesByStem);
    }

    public async Task<TerrainTextureGeoReferencedRasterSource?> TryResolveRasterSourceAsync(
        DemTerrainRasterCacheKey cacheKey,
        ThirdRegionalMeshCode meshCode,
        GeographicRectangle overlayBounds,
        CancellationToken cancellationToken)
    {
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
        ThirdRegionalMeshCode meshCode,
        GeographicRectangle overlayBounds,
        CancellationToken cancellationToken)
    {
        foreach (ITerrainTextureRasterContentSource rasterSource in ResolveCandidateRasterSources(meshCode))
        {
            GeoReferencedRasterMetadata? metadata = await TerrainTextureGeoReferencedRasterMetadataReader.TryReadMetadataAsync(
                rasterSource,
                cancellationToken);
            if (metadata is null || !Contains(metadata.GeographicBounds, overlayBounds))
            {
                continue;
            }

            return new TerrainTextureGeoReferencedRasterSource(rasterSource, metadata);
        }

        return null;
    }

    private void RemoveFaultedResolveTask(
        DemTerrainRasterCacheKey cacheKey,
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

    private ITerrainTextureRasterContentSource[] ResolveCandidateRasterSources(ThirdRegionalMeshCode meshCode)
    {
        if (directRasterPath is not null)
        {
            return [new LocalTerrainTextureRasterContentSource(directRasterPath)];
        }

        if (contentSource is null)
        {
            return [];
        }

        List<string> relativePaths = [];
        HashSet<string> seenRelativePaths = new(StringComparer.OrdinalIgnoreCase);
        if (relativeRasterPathsByStem.TryGetValue(meshCode.Value, out string? exactMatchPath)
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

        return relativePaths
            .Select(relativePath =>
                (ITerrainTextureRasterContentSource)new DatasetTerrainTextureRasterContentSource(
                    relativePath,
                    EnsureLocalRasterFileAsync))
            .ToArray();
    }

    private Task<string> EnsureLocalRasterFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        if (contentSource is null || outputRoot is null)
        {
            return Task.FromException<string>(
                new InvalidOperationException("Dataset raster sources require a dataset content source and local-file cache root."));
        }

        Task<string> localFilePathTask;
        lock (cachedLocalRasterFileTaskGate)
        {
            if (!cachedLocalRasterFileTasksByRelativePath.TryGetValue(relativePath, out localFilePathTask!))
            {
                localFilePathTask = contentSource.EnsureLocalFileAsync(relativePath, outputRoot, CancellationToken.None);
                cachedLocalRasterFileTasksByRelativePath[relativePath] = localFilePathTask;
                _ = localFilePathTask.ContinueWith(
                    completedTask => RemoveFaultedLocalRasterFileTask(relativePath, completedTask),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        return cancellationToken.CanBeCanceled
            ? localFilePathTask.WaitAsync(cancellationToken)
            : localFilePathTask;
    }

    private void RemoveFaultedLocalRasterFileTask(string relativePath, Task<string> completedTask)
    {
        if (!completedTask.IsFaulted && !completedTask.IsCanceled)
        {
            return;
        }

        lock (cachedLocalRasterFileTaskGate)
        {
            if (cachedLocalRasterFileTasksByRelativePath.TryGetValue(relativePath, out Task<string>? cachedTask)
                && ReferenceEquals(cachedTask, completedTask))
            {
                cachedLocalRasterFileTasksByRelativePath.Remove(relativePath);
            }
        }
    }

    private sealed class DatasetTerrainTextureRasterContentSource(
        string relativePath,
        Func<string, CancellationToken, Task<string>> ensureLocalFileAsync) : ITerrainTextureRasterContentSource
    {
        public string Description => relativePath;

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "The caller owns the returned stream and disposes it after raster decoding.")]
        public async ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string localFilePath = await ensureLocalFileAsync(relativePath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new FileStream(
                localFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                useAsync: true);
        }
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
