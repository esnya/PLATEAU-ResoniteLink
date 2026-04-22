using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class CkanPlateauDatasetSourceResolver : IPlateauDatasetSourceResolver
{
    private readonly HttpClient httpClient;
    private readonly IRemoteArchiveDistributionPolicy remoteArchiveDistributionPolicy;
    private readonly IArchiveFileLayoutPolicy archiveFileLayoutPolicy;

    public CkanPlateauDatasetSourceResolver(
        HttpClient httpClient,
        IRemoteArchiveDistributionPolicy remoteArchiveDistributionPolicy,
        IArchiveFileLayoutPolicy archiveFileLayoutPolicy)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.remoteArchiveDistributionPolicy = remoteArchiveDistributionPolicy ?? throw new ArgumentNullException(nameof(remoteArchiveDistributionPolicy));
        this.archiveFileLayoutPolicy = archiveFileLayoutPolicy ?? throw new ArgumentNullException(nameof(archiveFileLayoutPolicy));
    }

    public async Task<ValidatedPlateauImportRequest> ResolveAsync(
        ValidatedPlateauImportRequest request,
        string workRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        _ = archiveFileLayoutPolicy.CreateSafePathSegment(request.Dataset);
        _ = TryCreateSafePathSegment(request.MeshCode, out _);

        ValidatedDatasetLocation resolvedSource = await ResolveOptionalRemoteDatasetLocationAsync(
            request.Source,
            workRoot,
            resourcePrefix: "source-archive",
            invalidateLocalFileCache: true,
            cancellationToken) ?? throw new InvalidOperationException("The normalized CityGML source must resolve to a local or remote source.");
        ValidatedDatasetLocation? resolvedDemTextureSource = await ResolveOptionalRemoteDatasetLocationAsync(
            request.DemTextureSource,
            workRoot,
            resourcePrefix: "source-ortho",
            invalidateLocalFileCache: false,
            cancellationToken);

        return request with
        {
            Source = resolvedSource,
            DemTextureSource = resolvedDemTextureSource!,
        };
    }

    private async Task<ValidatedDatasetLocation?> ResolveOptionalRemoteDatasetLocationAsync(
        ValidatedDatasetLocation? source,
        string workRoot,
        string resourcePrefix,
        bool invalidateLocalFileCache,
        CancellationToken cancellationToken)
    {
        if (source is null || source is ValidatedLocalDatasetLocation)
        {
            return source;
        }

        ValidatedRemoteDatasetLocation remoteSource = (ValidatedRemoteDatasetLocation)source;
        string resourcePath = RemoteDatasetResourceLayout.GetRemoteResourcePath(
            workRoot,
            remoteSource.ServerUri,
            resourcePrefix);
        string metadataPath = remoteArchiveDistributionPolicy.GetSourceArchiveMetadataPath(resourcePath);

        CachedHttpContent cachedContent = await HttpFileCache.GetOrFetchAsync(
            httpClient,
            remoteSource.ServerUri,
            resourcePath,
            metadataPath,
            cancellationToken);
        if (invalidateLocalFileCache && cachedContent.Changed)
        {
            InvalidateTemporaryLocalFileCache(workRoot, resourcePath);
        }

        return new ValidatedLocalDatasetLocation(resourcePath);
    }

    private void InvalidateTemporaryLocalFileCache(string datasetRoot, string archivePath)
    {
        string runRoot = Path.Combine(Path.GetFullPath(datasetRoot), "run");
        if (!Directory.Exists(runRoot))
        {
            return;
        }

        string archiveCacheName = archiveFileLayoutPolicy.GetLocalFileCacheKey(archivePath);
        try
        {
            foreach (string localFileCacheRoot in Directory.EnumerateDirectories(
                         runRoot,
                         "local-file-cache",
                         SearchOption.AllDirectories))
            {
                TryDeleteDirectory(Path.Combine(localFileCacheRoot, archiveCacheName));
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private bool TryCreateSafePathSegment(string value, out string? safePathSegment)
    {
        try
        {
            safePathSegment = archiveFileLayoutPolicy.CreateSafePathSegment(value);
            return true;
        }
        catch (PlateauImportValidationException)
        {
            if (ContainsExplicitPathTraversal(value))
            {
                throw;
            }

            safePathSegment = null;
            return false;
        }
    }

    private static bool ContainsExplicitPathTraversal(string value)
    {
        string normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        return normalized.Contains("../", StringComparison.Ordinal)
            || normalized.Contains("..\\", StringComparison.Ordinal)
            || normalized.StartsWith("./", StringComparison.Ordinal)
            || normalized.StartsWith(".\\", StringComparison.Ordinal);
    }
}
