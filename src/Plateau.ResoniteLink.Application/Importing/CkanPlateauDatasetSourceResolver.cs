using System.IO.Compression;
using System.Net.Http.Json;
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

        Uri archiveUri = request.ServerUri is not null && LooksLikeArchiveUri(request.ServerUri)
            ? request.ServerUri
            : await DiscoverArchiveUriAsync(request, cancellationToken);

        string cacheRoot = Path.GetFullPath(Path.Combine(
            workRoot,
            "cache",
            "remote",
            CreateSafePathSegment(request.Dataset),
            CreateSafePathSegment(request.MeshCode)));

        Directory.CreateDirectory(cacheRoot);

        string archiveFileName = GetArchiveFileName(archiveUri);
        string archivePath = Path.Combine(cacheRoot, archiveFileName);
        string extractRoot = Path.Combine(cacheRoot, Path.GetFileNameWithoutExtension(archiveFileName));
        string completionMarkerPath = Path.Combine(extractRoot, ".extracted");
        string completionMarkerContents = GetExtractionMarkerContents(archiveUri);

        if (!File.Exists(archivePath))
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                archiveUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using Stream archiveStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream archiveFile = File.Create(archivePath);
            await archiveStream.CopyToAsync(archiveFile, cancellationToken);
        }

        if (!File.Exists(completionMarkerPath)
            || !string.Equals(
                await File.ReadAllTextAsync(completionMarkerPath, cancellationToken),
                completionMarkerContents,
                StringComparison.Ordinal))
        {
            if (Directory.Exists(extractRoot))
            {
                Directory.Delete(extractRoot, recursive: true);
            }

            await ExtractArchiveAsync(archivePath, extractRoot, cancellationToken);
            await File.WriteAllTextAsync(completionMarkerPath, completionMarkerContents, cancellationToken);
        }

        return request with
        {
            SourceKind = DatasetSourceKind.Local,
            LocalSourcePath = PlateauDatasetPathResolver.ResolveDatasetRoot(extractRoot),
        };
    }

    private async Task<Uri> DiscoverArchiveUriAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken)
    {
        string datasetSlug = NormalizeDatasetSlug(request.Dataset);
        string query = $"plateau-{datasetSlug}";
        Uri packageSearchUri = BuildPackageSearchUri(request.ServerUri ?? DefaultCatalogApiBaseUri, query);

        using HttpResponseMessage response = await httpClient.GetAsync(packageSearchUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        CkanPackageSearchResponse payload =
            await response.Content.ReadFromJsonAsync<CkanPackageSearchResponse>(JsonOptions, cancellationToken)
            ?? throw new PlateauImportValidationException(["The CKAN package search response was empty."]);

        string meshPrefix = request.MeshCode.Length >= 6 ? request.MeshCode[..6] : request.MeshCode;
        CkanPackage? package = payload.Result.Results
            .Select(candidate => new
            {
                Package = candidate,
                Score = ScorePackage(candidate, datasetSlug, meshPrefix),
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
            })
            .Where(static candidate => candidate.Score > 0)
            .OrderByDescending(static candidate => candidate.Score)
            .ThenBy(static candidate => candidate.Resource.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static candidate => candidate.Resource)
            .FirstOrDefault();

        if (resource is null || string.IsNullOrWhiteSpace(resource.Url))
        {
            throw new PlateauImportValidationException(
                [$"No downloadable CityGML archive could be found online for mesh code '{request.MeshCode}'."]);
        }

        if (!LooksLikeArchiveUri(new Uri(resource.Url, UriKind.Absolute)))
        {
            throw new PlateauImportValidationException(
                [$"The discovered online resource '{resource.Url}' is not a supported ZIP archive."]);
        }

        return new Uri(resource.Url, UriKind.Absolute);
    }

    private static Uri BuildPackageSearchUri(Uri apiBaseUri, string query)
    {
        string baseUri = apiBaseUri.ToString();
        if (!baseUri.EndsWith('/'))
        {
            baseUri += "/";
        }

        return new Uri(
            $"{baseUri}package_search?q={Uri.EscapeDataString(query)}&rows=50",
            UriKind.Absolute);
    }

    private static int ScorePackage(CkanPackage package, string datasetSlug, string meshPrefix)
    {
        int score = 0;
        string normalizedName = package.Name ?? string.Empty;
        string normalizedTitle = package.Title ?? string.Empty;

        if (normalizedName.Contains($"plateau-{datasetSlug}", StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

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

        return score;
    }

    private static int ScoreResource(CkanResource resource, string meshPrefix)
    {
        if (string.IsNullOrWhiteSpace(resource.Url))
        {
            return 0;
        }

        Uri resourceUri = new(resource.Url, UriKind.Absolute);
        if (!LooksLikeArchiveUri(resourceUri))
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

        return score;
    }

    private static bool LooksLikeArchiveUri(Uri uri)
    {
        return uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
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

    private static async Task ExtractArchiveAsync(
        string archivePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationPath);
        await ZipFile.ExtractToDirectoryAsync(
            archivePath,
            destinationPath,
            overwriteFiles: true,
            cancellationToken);

        string[] nestedArchives = Directory
            .EnumerateFiles(destinationPath, "*.zip", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (string nestedArchivePath in nestedArchives)
        {
            string nestedName = Path.GetFileNameWithoutExtension(nestedArchivePath);
            string nestedDestination = IsPlateauPackageName(nestedName)
                ? Path.Combine(destinationPath, "udx", nestedName)
                : Path.Combine(destinationPath, nestedName);

            await ExtractArchiveAsync(nestedArchivePath, nestedDestination, cancellationToken);
        }
    }

    private static string GetExtractionMarkerContents(Uri archiveUri)
    {
        return $"v3{Environment.NewLine}{archiveUri}";
    }

    private static bool IsPlateauPackageName(string value)
    {
        return PlateauPackageCatalog.TryNormalizePackageName(value, out _);
    }

    private static string NormalizeDatasetSlug(string dataset)
    {
        string normalized = dataset.Trim().ToLowerInvariant();
        normalized = DatasetSlugSeparatorRegex().Replace(normalized, "-");
        normalized = normalized.Trim('-');
        return normalized;
    }

    private static string CreateSafePathSegment(string value)
    {
        string normalized = value.Trim();
        return string.Concat(normalized.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex DatasetSlugSeparatorRegex();

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
}
