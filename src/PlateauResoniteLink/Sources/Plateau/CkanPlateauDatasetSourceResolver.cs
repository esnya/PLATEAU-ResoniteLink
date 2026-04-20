using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PlateauResoniteLink.Application.Importing;

public sealed class CkanPlateauDatasetSourceResolver : IPlateauDatasetSourceResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly IRemoteArchiveDistributionPolicy remoteArchiveDistributionPolicy;
    private readonly IArchiveFileLayoutPolicy archiveFileLayoutPolicy;

    public CkanPlateauDatasetSourceResolver(HttpClient httpClient)
        : this(httpClient, new RemoteArchiveDistributionPolicy(), new ArchiveFileLayoutPolicy())
    {
    }

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

        if (request.Source is ValidatedPlateauLocalImportSource)
        {
            return request with
            {
                DemTextureSource = await ResolveOptionalDemTextureSourceAsync(
                    request.DemTextureSource,
                    workRoot,
                    cancellationToken),
            };
        }

        ValidatedPlateauRemoteImportSource remoteSource = (ValidatedPlateauRemoteImportSource)request.Source;

        _ = archiveFileLayoutPolicy.CreateSafePathSegment(request.Dataset);
        _ = TryCreateSafePathSegment(request.MeshCode, out _);

        Uri archiveUri = remoteSource.ServerUri;

        string archiveFileName = remoteArchiveDistributionPolicy.GetArchiveFileName(archiveUri);
        string archivePath = remoteArchiveDistributionPolicy.GetSourceArchivePath(workRoot, archiveUri, archiveFileName);
        string archiveMetadataPath = remoteArchiveDistributionPolicy.GetSourceArchiveMetadataPath(archivePath);

        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);

        if (File.Exists(archivePath))
        {
            if (await TryReuseCachedArchiveAsync(workRoot, archiveUri, archivePath, archiveMetadataPath, cancellationToken))
            {
                return request with
                {
                    Source = new ValidatedPlateauLocalImportSource(archivePath),
                    DemTextureSource = await ResolveOptionalDemTextureSourceAsync(
                        request.DemTextureSource,
                        workRoot,
                        cancellationToken),
                };
            }
        }

        await DownloadArchiveAsync(workRoot, archiveUri, archivePath, archiveMetadataPath, cancellationToken);

        return request with
        {
            Source = new ValidatedPlateauLocalImportSource(archivePath),
            DemTextureSource = await ResolveOptionalDemTextureSourceAsync(
                request.DemTextureSource,
                workRoot,
                cancellationToken),
        };
    }

    private async Task<ValidatedPlateauImportSource?> ResolveOptionalDemTextureSourceAsync(
        ValidatedPlateauImportSource? source,
        string workRoot,
        CancellationToken cancellationToken)
    {
        if (source is null || source is ValidatedPlateauLocalImportSource)
        {
            return source;
        }

        ValidatedPlateauRemoteImportSource remoteSource = (ValidatedPlateauRemoteImportSource)source;
        Uri resourceUri = remoteSource.ServerUri;
        string resourcePath = RemoteDatasetResourceLayout.GetRemoteResourcePath(workRoot, resourceUri, "source-ortho");
        string metadataPath = RemoteDatasetResourceLayout.GetRemoteResourceMetadataPath(resourcePath);

        Directory.CreateDirectory(Path.GetDirectoryName(resourcePath)!);

        if (File.Exists(resourcePath))
        {
            if (await TryReuseCachedArchiveAsync(workRoot, resourceUri, resourcePath, metadataPath, cancellationToken))
            {
                return new ValidatedPlateauLocalImportSource(resourcePath);
            }
        }

        await DownloadArchiveAsync(workRoot, resourceUri, resourcePath, metadataPath, cancellationToken);
        return new ValidatedPlateauLocalImportSource(resourcePath);
    }

    private async Task<bool> TryReuseCachedArchiveAsync(
        string datasetRoot,
        Uri archiveUri,
        string archivePath,
        string metadataPath,
        CancellationToken cancellationToken)
    {
        ArchiveMetadata? metadata = await TryReadArchiveMetadataAsync(metadataPath, cancellationToken);
        if (metadata is null || (metadata.ETag is null && metadata.LastModifiedUtc is null))
        {
            return await TryRefreshCachedArchiveWithoutMetadataAsync(
                datasetRoot,
                archiveUri,
                archivePath,
                metadataPath,
                cancellationToken);
        }

        using HttpRequestMessage request = new(HttpMethod.Get, archiveUri)
        {
            Version = HttpVersion.Version11,
        };
        if (EntityTagHeaderValue.TryParse(metadata.ETag, out EntityTagHeaderValue? etag))
        {
            request.Headers.IfNoneMatch.Add(etag);
        }

        if (metadata.LastModifiedUtc is not null)
        {
            request.Headers.IfModifiedSince = metadata.LastModifiedUtc;
        }

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return true;
            }

            response.EnsureSuccessStatusCode();
            await WriteCachedArchiveResponseAsync(response, archivePath, metadataPath, cancellationToken);
            InvalidateTemporaryLocalFileCache(datasetRoot, archivePath);
            return true;
        }
        catch (HttpRequestException) when (File.Exists(archivePath))
        {
            return true;
        }
    }

    private async Task<bool> TryRefreshCachedArchiveWithoutMetadataAsync(
        string datasetRoot,
        Uri archiveUri,
        string archivePath,
        string metadataPath,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                archiveUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            await WriteCachedArchiveResponseAsync(response, archivePath, metadataPath, cancellationToken);
            InvalidateTemporaryLocalFileCache(datasetRoot, archivePath);
            return true;
        }
        catch (HttpRequestException) when (File.Exists(archivePath))
        {
            return true;
        }
    }

    private async Task DownloadArchiveAsync(
        string datasetRoot,
        Uri archiveUri,
        string archivePath,
        string metadataPath,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            archiveUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await WriteCachedArchiveResponseAsync(response, archivePath, metadataPath, cancellationToken);
        InvalidateTemporaryLocalFileCache(datasetRoot, archivePath);
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

    private static async Task WriteCachedArchiveResponseAsync(
        HttpResponseMessage response,
        string archivePath,
        string metadataPath,
        CancellationToken cancellationToken)
    {
        string temporaryArchivePath = $"{archivePath}.{Guid.NewGuid():N}.tmp";
        string temporaryMetadataPath = $"{metadataPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using Stream archiveStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream archiveFile = new(
                temporaryArchivePath,
                FileMode.Create,
                FileAccess.Write,
                    FileShare.None,
                    bufferSize: 16 * 1024,
                    useAsync: true);
            await archiveStream.CopyToAsync(archiveFile, cancellationToken);

            await archiveFile.FlushAsync(cancellationToken);
            archiveFile.Close();
            File.Move(temporaryArchivePath, archivePath, overwrite: true);

            ArchiveMetadata metadata = CreateArchiveMetadataFromResponse(response);
            await WriteArchiveMetadataAsync(metadataPath, temporaryMetadataPath, metadata, cancellationToken);
        }
        catch
        {
            if (File.Exists(temporaryArchivePath))
            {
                File.Delete(temporaryArchivePath);
            }

            if (File.Exists(temporaryMetadataPath))
            {
                File.Delete(temporaryMetadataPath);
            }

            throw;
        }
    }

    private static ArchiveMetadata CreateArchiveMetadataFromResponse(HttpResponseMessage response)
    {
        return new ArchiveMetadata(
            response.Headers.ETag?.Tag,
            response.Content.Headers.LastModified);
    }

    private static async Task<ArchiveMetadata?> TryReadArchiveMetadataAsync(
        string metadataPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            await using FileStream metadataFile = new(
                metadataPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                useAsync: true);

            return await JsonSerializer.DeserializeAsync<ArchiveMetadata>(metadataFile, JsonOptions, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task WriteArchiveMetadataAsync(
        string metadataPath,
        string temporaryMetadataPath,
        ArchiveMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using (FileStream metadataFile = new(
            temporaryMetadataPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            useAsync: true))
        {
            await JsonSerializer.SerializeAsync(metadataFile, metadata, JsonOptions, cancellationToken);
            await metadataFile.FlushAsync(cancellationToken);
        }

        try
        {
            File.Move(temporaryMetadataPath, metadataPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporaryMetadataPath))
            {
                File.Delete(temporaryMetadataPath);
            }

            throw;
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

    private sealed record ArchiveMetadata(
        string? ETag,
        DateTimeOffset? LastModifiedUtc);
}
