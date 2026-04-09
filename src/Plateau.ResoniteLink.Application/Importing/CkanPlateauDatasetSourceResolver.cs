using System.Net;
using System.Net.Http.Headers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed class CkanPlateauDatasetSourceResolver : IPlateauDatasetSourceResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;

    public CkanPlateauDatasetSourceResolver()
        : this(new HttpClient())
    {
    }

    public CkanPlateauDatasetSourceResolver(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<PlateauImportRequest> ResolveAsync(
        PlateauImportRequest request,
        string workRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        if (request.Source is PlateauLocalImportSource)
        {
            return request;
        }

        if (request.Source is not PlateauRemoteImportSource remoteSource)
        {
            throw new PlateauImportValidationException(
                ["Remote import requires --server-url to point directly to a .zip or .7z CityGML archive. Built-in dataset search is not supported."]);
        }

        if (remoteSource.ServerUri is null)
        {
            throw new PlateauImportValidationException(
                ["Remote import requires --server-url to point directly to a .zip or .7z CityGML archive. Built-in dataset search is not supported."]);
        }

        if (!LooksLikeSupportedArchiveUri(remoteSource.ServerUri))
        {
            throw new PlateauImportValidationException(
                [$"The direct archive URL '{remoteSource.ServerUri}' is not a supported archive. Supported extensions: .zip, .7z."]);
        }

        Uri archiveUri = remoteSource.ServerUri;

        string safeDataset = CreateSafePathSegment(request.Dataset);
        string safeMeshCode = CreateSafePathSegment(request.MeshCode);
        string archiveCacheKey = CreateArchiveCacheKey(archiveUri);
        string cacheRoot = GetArchiveCacheRoot(workRoot, safeDataset, archiveCacheKey);
        TryMigrateLegacyArchiveCache(workRoot, safeDataset, safeMeshCode, archiveCacheKey, cacheRoot);

        Directory.CreateDirectory(cacheRoot);

        string archiveFileName = GetArchiveFileName(archiveUri);
        string archivePath = Path.Combine(cacheRoot, archiveFileName);
        string archiveMetadataPath = $"{archivePath}.{CreateArchiveMetadataFileName()}";

        if (File.Exists(archivePath))
        {
            if (await TryReuseCachedArchiveAsync(archiveUri, archivePath, archiveMetadataPath, cancellationToken))
            {
                return request with
                {
                    Source = new PlateauLocalImportSource(archivePath),
                };
            }
        }

        await DownloadArchiveAsync(archiveUri, archivePath, archiveMetadataPath, cancellationToken);

        return request with
        {
            Source = new PlateauLocalImportSource(archivePath),
        };
    }

    private async Task<bool> TryReuseCachedArchiveAsync(
        Uri archiveUri,
        string archivePath,
        string metadataPath,
        CancellationToken cancellationToken)
    {
        ArchiveMetadata? metadata = await TryReadArchiveMetadataAsync(metadataPath, cancellationToken);
        if (metadata is null || (metadata.ETag is null && metadata.LastModifiedUtc is null))
        {
            return await TryRefreshCachedArchiveWithoutMetadataAsync(
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
            InvalidateMaterializedCache(archivePath);
            return true;
        }
        catch (HttpRequestException) when (File.Exists(archivePath))
        {
            return true;
        }
    }

    private async Task<bool> TryRefreshCachedArchiveWithoutMetadataAsync(
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
            InvalidateMaterializedCache(archivePath);
            return true;
        }
        catch (HttpRequestException) when (File.Exists(archivePath))
        {
            return true;
        }
    }

    private async Task DownloadArchiveAsync(
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
        InvalidateMaterializedCache(archivePath);
    }

    private static void InvalidateMaterializedCache(string archivePath)
    {
        string? cacheDirectory = Path.GetDirectoryName(archivePath);
        while (!string.IsNullOrWhiteSpace(cacheDirectory))
        {
            if (string.Equals(Path.GetFileName(cacheDirectory), "cache", StringComparison.Ordinal))
            {
                string? workRoot = Path.GetDirectoryName(cacheDirectory);
                if (string.IsNullOrWhiteSpace(workRoot))
                {
                    return;
                }

                InvalidateMaterializedCacheUnder(workRoot, archivePath);
                return;
            }

            cacheDirectory = Path.GetDirectoryName(cacheDirectory);
        }
    }

    private static void InvalidateMaterializedCacheUnder(string workRoot, string archivePath)
    {
        foreach (string archiveCacheName in PlateauDatasetContentSourceFactory.GetMaterializedArchiveCacheKeys(archivePath))
        {
            TryDeleteDirectory(Path.Combine(workRoot, ".dataset-cache", archiveCacheName));
            TryDeleteDirectory(Path.Combine(workRoot, ".generated-assets", ".dataset-cache", archiveCacheName));

            try
            {
                foreach (string materializedCacheRoot in Directory.EnumerateDirectories(
                             workRoot,
                             ".dataset-cache",
                             SearchOption.AllDirectories))
                {
                    TryDeleteDirectory(Path.Combine(materializedCacheRoot, archiveCacheName));
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
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

    private static string CreateArchiveMetadataFileName() => "meta.json";

    private static bool LooksLikeSupportedArchiveUri(Uri uri)
    {
        return TryGetArchiveKind(uri.AbsolutePath, out _);
    }

    private static string GetArchiveFileName(Uri archiveUri)
    {
        string fileName = Path.GetFileName(archiveUri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new PlateauImportValidationException(
                [$"The online resource '{archiveUri}' does not expose a valid archive file name."]);
        }

        return fileName;
    }

    private static string CreateSafePathSegment(string value)
    {
        string normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new PlateauImportValidationException([$"Invalid path segment '{value}'."]);
        }

        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        invalidCharacters = invalidCharacters
            .Concat(new[] { '/', '\\', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar })
            .Distinct()
            .ToArray();

        if (normalized.IndexOfAny(invalidCharacters) >= 0
            || normalized.Equals("..", StringComparison.Ordinal)
            || normalized.Equals(".", StringComparison.Ordinal))
        {
            throw new PlateauImportValidationException([$"Invalid path segment '{value}'."]);
        }

        return normalized;
    }

    private static string CreateArchiveCacheKey(Uri archiveUri)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(archiveUri.ToString()));

        StringBuilder builder = new(capacity: digest.Length * 2);
        for (int index = 0; index < digest.Length; index++)
        {
            _ = builder.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string GetArchiveCacheRoot(string workRoot, string safeDataset, string archiveCacheKey)
    {
        return Path.GetFullPath(Path.Combine(
            workRoot,
            "cache",
            "remote",
            safeDataset,
            archiveCacheKey));
    }

    private static string GetLegacyArchiveCacheRoot(
        string workRoot,
        string safeDataset,
        string safeMeshCode,
        string archiveCacheKey)
    {
        return Path.GetFullPath(Path.Combine(
            workRoot,
            "cache",
            "remote",
            safeDataset,
            safeMeshCode,
            archiveCacheKey));
    }

    private static void TryMigrateLegacyArchiveCache(
        string workRoot,
        string safeDataset,
        string safeMeshCode,
        string archiveCacheKey,
        string cacheRoot)
    {
        if (Directory.Exists(cacheRoot))
        {
            return;
        }

        string legacyCacheRoot = GetLegacyArchiveCacheRoot(workRoot, safeDataset, safeMeshCode, archiveCacheKey);
        if (!Directory.Exists(legacyCacheRoot))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(cacheRoot)!);
        Directory.Move(legacyCacheRoot, cacheRoot);
        TryDeleteDirectory(Path.GetDirectoryName(legacyCacheRoot));
    }

    private static bool TryGetArchiveKind(string path, out SupportedArchiveKind archiveKind)
    {
        string extension = Path.GetExtension(path);
        if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            archiveKind = SupportedArchiveKind.Zip;
            return true;
        }

        if (string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase))
        {
            archiveKind = SupportedArchiveKind.SevenZip;
            return true;
        }

        archiveKind = default;
        return false;
    }

    private enum SupportedArchiveKind
    {
        Zip,
        SevenZip,
    }

    private sealed record ArchiveMetadata(
        string? ETag,
        DateTimeOffset? LastModifiedUtc);
}
