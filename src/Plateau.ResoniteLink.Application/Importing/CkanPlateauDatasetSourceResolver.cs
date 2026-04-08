using System.Net.Http.Json;
using System.Net;
using System.Net.Http.Headers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed partial class CkanPlateauDatasetSourceResolver : IPlateauDatasetSourceResolver
{
    private static readonly Uri DefaultCatalogApiBaseUri = new("https://search.ckan.jp/backend/api/", UriKind.Absolute);
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

        if (request.SourceKind == DatasetSourceKind.Local)
        {
            return request;
        }

        Uri? archiveUri = null;
        if (request.ServerUri is not null)
        {
            if (LooksLikeSupportedArchiveUri(request.ServerUri))
            {
                archiveUri = request.ServerUri;
            }
            else if (LooksLikeDirectArchiveUri(request.ServerUri))
            {
                throw new PlateauImportValidationException(
                    [$"The direct archive URL '{request.ServerUri}' is not a supported archive. Supported extensions: .zip, .7z."]);
            }
        }

        archiveUri ??= await DiscoverArchiveUriAsync(request, cancellationToken);

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
                    SourceKind = DatasetSourceKind.Local,
                    LocalSourcePath = archivePath,
                };
            }
        }

        await DownloadArchiveAsync(archiveUri, archivePath, archiveMetadataPath, cancellationToken);

        return request with
        {
            SourceKind = DatasetSourceKind.Local,
            LocalSourcePath = archivePath,
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
            return false;
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

    private async Task DownloadArchiveAsync(
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
        }
        catch
        {
            throw;
        }
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

    private static string CreateArchiveMetadataFileName()
    {
        return "meta.json";
    }

    private async Task<Uri> DiscoverArchiveUriAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> datasetSlugVariants = GetDatasetSlugVariants(request.Dataset);
        IReadOnlyList<CkanPackage> packages = await SearchPackagesAsync(
            request.ServerUri ?? DefaultCatalogApiBaseUri,
            request.Dataset,
            datasetSlugVariants,
            cancellationToken);
        string meshPrefix = request.MeshCode.Length >= 6 ? request.MeshCode[..6] : request.MeshCode;
        CkanPackage? package = packages
            .Select(candidate => new
            {
                Package = candidate,
                Score = ScorePackage(candidate, request.Dataset, datasetSlugVariants, meshPrefix),
            })
            .Where(static candidate => candidate.Score > 0)
            .OrderByDescending(static candidate => candidate.Score)
            .ThenBy(static candidate => candidate.Package.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static candidate => candidate.Package)
            .FirstOrDefault();

        if (package is null)
        {
            throw new PlateauImportValidationException(
                [$"No official PLATEAU CityGML dataset could be found online for '{request.Dataset}'."]);
        }

        CkanResource? resource = package.Resources
            .Select(candidate => new
            {
                Resource = candidate,
                Score = ScoreResource(candidate, meshPrefix),
                Version = ExtractCityGmlVersion(candidate),
            })
            .Where(static candidate => candidate.Score > 0)
            .OrderByDescending(static candidate => candidate.Score)
            .ThenByDescending(static candidate => candidate.Version)
            .ThenBy(static candidate => candidate.Resource.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static candidate => candidate.Resource)
            .FirstOrDefault();

        if (resource is null || string.IsNullOrWhiteSpace(resource.Url))
        {
            throw new PlateauImportValidationException(
                [$"No downloadable CityGML archive could be found online for mesh code '{request.MeshCode}'."]);
        }

        if (!LooksLikeSupportedArchiveUri(new Uri(resource.Url, UriKind.Absolute)))
        {
            throw new PlateauImportValidationException(
                [$"The discovered online resource '{resource.Url}' is not a supported archive."]);
        }

        return new Uri(resource.Url, UriKind.Absolute);
    }

    private async Task<IReadOnlyList<CkanPackage>> SearchPackagesAsync(
        Uri apiBaseUri,
        string rawDataset,
        IReadOnlyList<string> datasetSlugVariants,
        CancellationToken cancellationToken)
    {
        List<CkanPackage> packages = [];
        foreach (string query in BuildPackageSearchQueries(rawDataset, datasetSlugVariants))
        {
            Uri packageSearchUri = BuildPackageSearchUri(apiBaseUri, query);
            using HttpResponseMessage response = await httpClient.GetAsync(packageSearchUri, cancellationToken);
            response.EnsureSuccessStatusCode();

            CkanPackageSearchResponse payload =
                await response.Content.ReadFromJsonAsync<CkanPackageSearchResponse>(JsonOptions, cancellationToken)
                ?? throw new PlateauImportValidationException(["The CKAN package search response was empty."]);

            packages.AddRange(payload.Result.Results);
        }

        return packages
            .DistinctBy(static package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> BuildPackageSearchQueries(
        string rawDataset,
        IReadOnlyList<string> datasetSlugVariants)
    {
        foreach (string datasetSlug in datasetSlugVariants)
        {
            yield return $"name:plateau-*{datasetSlug}*";
            yield return $"plateau-{datasetSlug}";
        }

        if (rawDataset.Any(static character => character > 127))
        {
            yield return $"title:{rawDataset} AND tags:PLATEAU";
        }
    }

    private static Uri BuildPackageSearchUri(Uri apiBaseUri, string query)
    {
        return new Uri(
            $"{EnsureTrailingSlash(apiBaseUri)}package_search?q={Uri.EscapeDataString(query)}&rows=200",
            UriKind.Absolute);
    }

    private static string EnsureTrailingSlash(Uri apiBaseUri)
    {
        string baseUri = apiBaseUri.ToString();
        return baseUri.EndsWith('/') ? baseUri : $"{baseUri}/";
    }

    private static int ScorePackage(
        CkanPackage package,
        string rawDataset,
        IReadOnlyList<string> datasetSlugVariants,
        string meshPrefix)
    {
        int score = 0;
        string normalizedName = package.Name ?? string.Empty;
        string normalizedTitle = package.Title ?? string.Empty;

        if (normalizedName.Contains("citygml", StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }

        if (normalizedTitle.Contains("CityGML", StringComparison.OrdinalIgnoreCase))
        {
            score += 25;
        }

        if (package.Resources.Any(resource => ScoreResource(resource, meshPrefix) > 0))
        {
            score += 25;
        }

        if (rawDataset.Any(static character => character > 127)
            && normalizedTitle.Contains(rawDataset, StringComparison.OrdinalIgnoreCase))
        {
            score += 150;
        }

        foreach (string datasetSlug in datasetSlugVariants)
        {
            if (normalizedName.Contains($"plateau-{datasetSlug}", StringComparison.OrdinalIgnoreCase))
            {
                score += 150;
            }
            else if (normalizedName.Contains(datasetSlug, StringComparison.OrdinalIgnoreCase))
            {
                score += 60;
            }
        }

        return score;
    }

    private static int ScoreResource(CkanResource resource, string meshPrefix)
    {
        if (string.IsNullOrWhiteSpace(resource.Url))
        {
            return 0;
        }

        Uri resourceUri = new(resource.Url, UriKind.Absolute);
        if (!LooksLikeSupportedArchiveUri(resourceUri))
        {
            return 0;
        }

        string name = resource.Name ?? string.Empty;
        string description = resource.Description ?? string.Empty;
        string format = resource.Format ?? string.Empty;

        int score = 0;
        if (string.Equals(name, meshPrefix, StringComparison.OrdinalIgnoreCase))
        {
            score += 200;
        }

        if (name.Contains(meshPrefix, StringComparison.OrdinalIgnoreCase))
        {
            score += 120;
        }

        if (description.Contains(meshPrefix, StringComparison.OrdinalIgnoreCase))
        {
            score += 80;
        }

        if (name.Contains("CityGML", StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
        }

        if (description.Contains("CityGML", StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        if (string.Equals(format, "ZIP", StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        if (string.Equals(format, "7Z", StringComparison.OrdinalIgnoreCase)
            || string.Equals(format, "7ZIP", StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        int version = ExtractCityGmlVersion(resource);
        if (version > 0)
        {
            score += version * 10;
        }
        else if (name.Contains("CityGML", StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        return score;
    }

    private static int ExtractCityGmlVersion(CkanResource resource)
    {
        return Math.Max(
            Math.Max(
                ExtractVersion(resource.Name ?? string.Empty),
                ExtractVersion(resource.Description ?? string.Empty)),
            ExtractVersion(resource.Url ?? string.Empty));
    }

    private static int ExtractVersion(string value)
    {
        MatchCollection matches = DatasetVersionRegex().Matches(value);
        int version = 0;
        foreach (Match match in matches.Cast<Match>())
        {
            if (int.TryParse(match.Groups["version"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedVersion))
            {
                version = Math.Max(version, parsedVersion);
            }
        }

        return version;
    }

    private static bool LooksLikeSupportedArchiveUri(Uri uri)
    {
        return TryGetArchiveKind(uri.AbsolutePath, out _);
    }

    private static bool LooksLikeDirectArchiveUri(Uri uri)
    {
        return !string.IsNullOrEmpty(Path.GetExtension(uri.AbsolutePath));
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

    private static string NormalizeDatasetSlug(string dataset)
    {
        string normalized = dataset.Trim().ToLowerInvariant();
        normalized = DatasetSlugSeparatorRegex().Replace(normalized, "-");
        normalized = normalized.Trim('-');
        return normalized;
    }

    private static string[] GetDatasetSlugVariants(string dataset)
    {
        HashSet<string> variants = new(StringComparer.OrdinalIgnoreCase);
        string normalized = NormalizeDatasetSlug(dataset);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            variants.Add(normalized);
            foreach (string suffix in MunicipalitySuffixes)
            {
                if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                    && !normalized.EndsWith($"-{suffix}", StringComparison.OrdinalIgnoreCase)
                    && normalized.Length > suffix.Length)
                {
                    variants.Add($"{normalized[..^suffix.Length]}-{suffix}");
                }
            }
        }

        return variants.ToArray();
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

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex DatasetSlugSeparatorRegex();

    [GeneratedRegex(@"(?:^|[^a-z0-9])v(?<version>\d+)(?:[^a-z0-9]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DatasetVersionRegex();

    private static readonly string[] MunicipalitySuffixes =
    [
        "shi",
        "ku",
        "cho",
        "machi",
        "son",
        "mura",
        "gun",
        "to",
        "do",
        "fu",
        "ken",
    ];

    private sealed record CkanPackageSearchResponse(CkanSearchResult Result);

    private sealed record CkanSearchResult(IReadOnlyList<CkanPackage> Results);

    private sealed record CkanPackage(
        string Name,
        string Title,
        IReadOnlyList<CkanResource> Resources);

    private sealed record CkanResource(
        string Name,
        string Description,
        string Format,
        string Url);

    private sealed record ArchiveMetadata(
        string? ETag,
        DateTimeOffset? LastModifiedUtc);
}
