using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class CkanPlateauDatasetSourceResolverCacheTests
{
    [Theory]
    [InlineData("..", "533944")]
    [InlineData("tokyo23ku", "../533944")]
    [InlineData("tokyo/23ku", "533944")]
    [InlineData("tokyo23ku\\..", "533944")]
    [InlineData("tokyo23ku", "533944/../../temp")]
    public async Task ResolveAsyncRejectsPathTraversalInDatasetOrMesh(string dataset, string mesh)
    {
        using StubHttpMessageHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using HttpClient httpClient = new(handler);
        CkanPlateauDatasetSourceResolver resolver = new(httpClient);
        using TemporaryDirectory workRoot = new();

        await Assert.ThrowsAsync<PlateauImportValidationException>(
            () => resolver.ResolveAsync(
                new PlateauImportRequest(
                    Dataset: dataset,
                    MeshCode: mesh,
                    SourceKind: DatasetSourceKind.Remote,
                    LocalSourcePath: null,
                    ServerUri: new Uri("https://example.test/direct.zip", UriKind.Absolute)),
                workRoot.Path));
    }

    [Fact]
    public async Task ResolveAsyncAllowsRegexEscapesInRemoteMeshCode()
    {
        byte[] zipBytes = CreateZipArchive(("udx/bldg/533944/plateau_tokyo23ku_bldg_533944.gml", "<CityModel />"));
        using TemporaryDirectory workRoot = new();
        using StubHttpMessageHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(zipBytes),
        });
        using HttpClient httpClient = new(handler);
        CkanPlateauDatasetSourceResolver resolver = new(httpClient);

        PlateauImportRequest resolved = await resolver.ResolveAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: @"^533944\d$",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri("https://example.test/direct.zip", UriKind.Absolute)),
            workRoot.Path);

        Assert.NotNull(resolved.LocalSourcePath);
        Assert.True(File.Exists(resolved.LocalSourcePath));
    }

    [Fact]
    public async Task ResolveAsyncAllowsRegexContainingSlashWhenMeshValueIsNotUsedAsPathSegment()
    {
        byte[] zipBytes = CreateZipArchive(("udx/bldg/533944/plateau_tokyo23ku_bldg_533944.gml", "<CityModel />"));
        using TemporaryDirectory workRoot = new();
        using StubHttpMessageHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(zipBytes),
        });
        using HttpClient httpClient = new(handler);
        CkanPlateauDatasetSourceResolver resolver = new(httpClient);

        PlateauImportRequest resolved = await resolver.ResolveAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "533944|533945/branch",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri("https://example.test/direct.zip", UriKind.Absolute)),
            workRoot.Path);

        Assert.NotNull(resolved.LocalSourcePath);
        Assert.True(File.Exists(resolved.LocalSourcePath));
    }

    [Fact]
    public async Task ResolveAsyncUsesUriHashInCachePathToAvoidFilenameCollisions()
    {
        byte[] zipBytes = CreateZipArchive(("udx/bldg/533944/plateau_tokyo23ku_bldg_533944.gml", "<CityModel />"));
        using TemporaryDirectory workRoot = new();
        using StubHttpMessageHandler handler = new(request =>
        {
            if (request.RequestUri is not null
                && string.Equals(
                    request.RequestUri.AbsoluteUri,
                    "https://example-a.test/533944.zip",
                    StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zipBytes) };
            }

            if (request.RequestUri is not null
                && string.Equals(
                    request.RequestUri.AbsoluteUri,
                    "https://example-b.test/533944.zip",
                    StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zipBytes) };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using HttpClient httpClient = new(handler);
        CkanPlateauDatasetSourceResolver resolver = new(httpClient);

        PlateauImportRequest firstRequest = await resolver.ResolveAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "533944",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri("https://example-a.test/533944.zip", UriKind.Absolute)),
            workRoot.Path);

        PlateauImportRequest secondRequest = await resolver.ResolveAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "533944",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri("https://example-b.test/533944.zip", UriKind.Absolute)),
            workRoot.Path);

        Assert.NotNull(firstRequest.LocalSourcePath);
        Assert.NotNull(secondRequest.LocalSourcePath);
        Assert.NotEqual(firstRequest.LocalSourcePath, secondRequest.LocalSourcePath);
        Assert.Equal("533944.zip", Path.GetFileName(firstRequest.LocalSourcePath));
        Assert.Equal("533944.zip", Path.GetFileName(secondRequest.LocalSourcePath));
        Assert.True(File.Exists(firstRequest.LocalSourcePath));
        Assert.True(File.Exists(secondRequest.LocalSourcePath));
        Assert.NotEqual(
            Path.GetDirectoryName(firstRequest.LocalSourcePath),
            Path.GetDirectoryName(secondRequest.LocalSourcePath));
    }

    [Fact]
    public async Task ResolveAsyncReusesCachedArchiveAcrossMeshCodeChanges()
    {
        byte[] zipBytes = CreateZipArchive(("udx/bldg/533944/plateau_tokyo23ku_bldg_533944.gml", "<CityModel />"));
        int requestCount = 0;

        using TemporaryDirectory workRoot = new();
        using StubHttpMessageHandler handler = new(request =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipBytes),
            };
        });
        using HttpClient httpClient = new(handler);
        CkanPlateauDatasetSourceResolver resolver = new(httpClient);
        Uri archiveUri = new("https://example.test/533944.zip", UriKind.Absolute);

        PlateauImportRequest firstRequest = await resolver.ResolveAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "533944",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: archiveUri),
            workRoot.Path);

        PlateauImportRequest secondRequest = await resolver.ResolveAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: archiveUri),
            workRoot.Path);

        Assert.NotNull(firstRequest.LocalSourcePath);
        Assert.Equal(firstRequest.LocalSourcePath, secondRequest.LocalSourcePath);
        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task ResolveAsyncMigratesLegacyMeshScopedCache()
    {
        byte[] zipBytes = CreateZipArchive(("udx/bldg/533944/plateau_tokyo23ku_bldg_533944.gml", "<CityModel />"));
        using TemporaryDirectory workRoot = new();
        Uri archiveUri = new("https://example.test/533944.zip", UriKind.Absolute);
        string archiveHash = CreateArchiveCacheKey(archiveUri);
        string legacyCacheRoot = Path.Combine(workRoot.Path, "cache", "remote", "tokyo23ku", "533944", archiveHash);
        Directory.CreateDirectory(legacyCacheRoot);
        string legacyArchivePath = Path.Combine(legacyCacheRoot, "533944.zip");
        string legacyMetadataPath = $"{legacyArchivePath}.meta.json";
        await File.WriteAllBytesAsync(legacyArchivePath, zipBytes);
        await File.WriteAllTextAsync(legacyMetadataPath, """{"etag":"\"v1\"","lastModifiedUtc":"2026-01-01T00:00:00+00:00"}""");

        using StubHttpMessageHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.NotModified));
        using HttpClient httpClient = new(handler);
        CkanPlateauDatasetSourceResolver resolver = new(httpClient);

        PlateauImportRequest resolvedRequest = await resolver.ResolveAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "533944",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: archiveUri),
            workRoot.Path);

        string newArchivePath = Path.Combine(workRoot.Path, "cache", "remote", "tokyo23ku", archiveHash, "533944.zip");
        Assert.Equal(newArchivePath, resolvedRequest.LocalSourcePath);
        Assert.True(File.Exists(newArchivePath));
        Assert.True(File.Exists($"{newArchivePath}.meta.json"));
        Assert.False(Directory.Exists(legacyCacheRoot));
    }

    [Fact]
    public async Task ResolveAsyncMigratesRegexRequestWithoutDeletingSiblingLegacyCaches()
    {
        byte[] zipBytes = CreateZipArchive(("udx/bldg/533944/plateau_tokyo23ku_bldg_533944.gml", "<CityModel />"));
        using TemporaryDirectory workRoot = new();
        Uri archiveUri = new("https://example.test/533944.zip", UriKind.Absolute);
        string archiveHash = CreateArchiveCacheKey(archiveUri);
        string legacyMeshRoot = Path.Combine(workRoot.Path, "cache", "remote", "tokyo23ku", "533944");
        string legacyCacheRoot = Path.Combine(legacyMeshRoot, archiveHash);
        Directory.CreateDirectory(legacyCacheRoot);
        string legacyArchivePath = Path.Combine(legacyCacheRoot, "533944.zip");
        string legacyMetadataPath = $"{legacyArchivePath}.meta.json";
        await File.WriteAllBytesAsync(legacyArchivePath, zipBytes);
        await File.WriteAllTextAsync(legacyMetadataPath, """{"etag":"\"v1\"","lastModifiedUtc":"2026-01-01T00:00:00+00:00"}""");

        string siblingArchiveHash = CreateArchiveCacheKey(new Uri("https://example.test/other.zip", UriKind.Absolute));
        string siblingLegacyCacheRoot = Path.Combine(legacyMeshRoot, siblingArchiveHash);
        Directory.CreateDirectory(siblingLegacyCacheRoot);
        await File.WriteAllTextAsync(Path.Combine(siblingLegacyCacheRoot, "other.zip"), "placeholder");

        using StubHttpMessageHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.NotModified));
        using HttpClient httpClient = new(handler);
        CkanPlateauDatasetSourceResolver resolver = new(httpClient);

        PlateauImportRequest resolvedRequest = await resolver.ResolveAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394\\d",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: archiveUri),
            workRoot.Path);

        string newArchivePath = Path.Combine(workRoot.Path, "cache", "remote", "tokyo23ku", archiveHash, "533944.zip");
        Assert.Equal(newArchivePath, resolvedRequest.LocalSourcePath);
        Assert.True(File.Exists(newArchivePath));
        Assert.True(Directory.Exists(siblingLegacyCacheRoot));
    }

    [Fact]
    public async Task ResolveAsyncRetriesDownloadAfterInterruptionWithoutLeavingArchive()
    {
        byte[] zipBytes = CreateZipArchive(("udx/bldg/533944/plateau_tokyo23ku_bldg_533944.gml", "<CityModel />"));
        bool hasReturnedFailure = false;
        using TemporaryDirectory workRoot = new();
        using StubHttpMessageHandler handler = new(request =>
        {
            if (hasReturnedFailure)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(zipBytes),
                };
            }

            if (request.RequestUri is null
                || !string.Equals(
                    request.RequestUri.AbsoluteUri,
                    "https://example.test/533944.zip",
                    StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            hasReturnedFailure = true;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new FailingHttpContent(Array.Empty<byte>(), 1),
            };
        });

        using HttpClient httpClient = new(handler);
        CkanPlateauDatasetSourceResolver resolver = new(httpClient);
        Uri archiveUri = new("https://example.test/533944.zip", UriKind.Absolute);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => resolver.ResolveAsync(
                new PlateauImportRequest(
                    Dataset: "tokyo23ku",
                    MeshCode: "533944",
                    SourceKind: DatasetSourceKind.Remote,
                    LocalSourcePath: null,
                    ServerUri: archiveUri),
                workRoot.Path));

        Assert.Empty(Directory.EnumerateFiles(workRoot.Path, "533944.zip", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(workRoot.Path, "*.tmp", SearchOption.AllDirectories));

        PlateauImportRequest resolvedRequest = await resolver.ResolveAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "533944",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: archiveUri),
            workRoot.Path);

        Assert.NotNull(resolvedRequest.LocalSourcePath);
        Assert.True(File.Exists(resolvedRequest.LocalSourcePath));
        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(resolvedRequest.LocalSourcePath);
        Assert.Contains("udx/bldg/533944/plateau_tokyo23ku_bldg_533944.gml", datasetSource.EnumerateFiles());
        Assert.Empty(Directory.EnumerateFiles(workRoot.Path, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ResolveAsyncRefreshesCachedArchiveWhenSourceChangesAtSameUri()
    {
        byte[] firstZipBytes = CreateZipArchive(
            ("udx/bldg/533944/plateau_tokyo23ku_bldg_533944.gml", "<CityModel>version-a</CityModel>"));
        byte[] secondZipBytes = CreateZipArchive(
            ("udx/bldg/533944/plateau_tokyo23ku_bldg_533944.gml", "<CityModel>version-b</CityModel>"));

        const string eTagA = "\"v1\"";
        const string eTagB = "\"v2\"";
        DateTimeOffset lastModifiedA = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset lastModifiedB = new(2026, 1, 1, 1, 0, 0, TimeSpan.Zero);
        int requestIndex = 0;

        using StubHttpMessageHandler handler = new(request =>
        {
            if (request.RequestUri is null
                || !string.Equals(
                    request.RequestUri.AbsoluteUri,
                    "https://example.test/533944.zip",
                    StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            requestIndex++;
            if (requestIndex == 1)
            {
                Assert.Empty(request.Headers.IfNoneMatch);
                HttpResponseMessage firstResponse = new(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(firstZipBytes),
                };
                firstResponse.Headers.ETag = new EntityTagHeaderValue(eTagA);
                firstResponse.Content.Headers.LastModified = lastModifiedA;
                return firstResponse;
            }

            Assert.NotEmpty(request.Headers.IfNoneMatch);
            Assert.Equal(eTagA, request.Headers.IfNoneMatch.First().Tag);
            Assert.Equal(lastModifiedA, request.Headers.IfModifiedSince);

            HttpResponseMessage secondResponse = new(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(secondZipBytes),
            };
            secondResponse.Headers.ETag = new EntityTagHeaderValue(eTagB);
            secondResponse.Content.Headers.LastModified = lastModifiedB;
            return secondResponse;
        });
        using HttpClient httpClient = new(handler);
        CkanPlateauDatasetSourceResolver resolver = new(httpClient);
        using TemporaryDirectory workRoot = new();

        PlateauImportRequest firstRequest = await resolver.ResolveAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "533944",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri("https://example.test/533944.zip", UriKind.Absolute)),
            workRoot.Path);

        Assert.NotNull(firstRequest.LocalSourcePath);
        string materializedCachePath = CreateStaleMaterializedCacheDirectory(
            workRoot.Path,
            firstRequest.LocalSourcePath);

        PlateauImportRequest secondRequest = await resolver.ResolveAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "533944",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri("https://example.test/533944.zip", UriKind.Absolute)),
            workRoot.Path);

        Assert.NotNull(firstRequest.LocalSourcePath);
        Assert.NotNull(secondRequest.LocalSourcePath);
        Assert.Equal(firstRequest.LocalSourcePath, secondRequest.LocalSourcePath);
        Assert.Equal(
            "<CityModel>version-b</CityModel>",
            ReadZipEntry(secondRequest.LocalSourcePath, "udx/bldg/533944/plateau_tokyo23ku_bldg_533944.gml"));
        Assert.Equal(2, requestIndex);
        Assert.False(Directory.Exists(materializedCachePath));
        Assert.False(Directory.Exists(Path.Combine(materializedCachePath, "udx")));
    }

    [Fact]
    public async Task ResolveAsyncReusesCachedArchiveWhenServerReturnsNotModified()
    {
        byte[] zipBytes = CreateZipArchive(
            ("udx/bldg/533944/plateau_tokyo23ku_bldg_533944.gml", "<CityModel>version-a</CityModel>"));

        const string eTag = "\"v1\"";
        DateTimeOffset lastModified = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        int requestIndex = 0;

        using StubHttpMessageHandler handler = new(request =>
        {
            requestIndex++;
            if (requestIndex == 1)
            {
                HttpResponseMessage firstResponse = new(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(zipBytes),
                };
                firstResponse.Headers.ETag = new EntityTagHeaderValue(eTag);
                firstResponse.Content.Headers.LastModified = lastModified;
                return firstResponse;
            }

            Assert.Equal(eTag, request.Headers.IfNoneMatch.Single().Tag);
            Assert.Equal(lastModified, request.Headers.IfModifiedSince);
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        });
        using HttpClient httpClient = new(handler);
        CkanPlateauDatasetSourceResolver resolver = new(httpClient);
        using TemporaryDirectory workRoot = new();

        PlateauImportRequest firstRequest = await resolver.ResolveAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "533944",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri("https://example.test/533944.zip", UriKind.Absolute)),
            workRoot.Path);

        Assert.NotNull(firstRequest.LocalSourcePath);
        string firstArchivePath = firstRequest.LocalSourcePath;
        DateTime firstWriteUtc = File.GetLastWriteTimeUtc(firstArchivePath);

        string materializedCachePath = CreateStaleMaterializedCacheDirectory(
            workRoot.Path,
            firstArchivePath);

        PlateauImportRequest secondRequest = await resolver.ResolveAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "533944",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri("https://example.test/533944.zip", UriKind.Absolute)),
            workRoot.Path);

        Assert.Equal(firstArchivePath, secondRequest.LocalSourcePath);
        Assert.NotNull(secondRequest.LocalSourcePath);
        string secondArchivePath = secondRequest.LocalSourcePath;
        Assert.Equal(
            "<CityModel>version-a</CityModel>",
            ReadZipEntry(secondArchivePath, "udx/bldg/533944/plateau_tokyo23ku_bldg_533944.gml"));
        Assert.Equal(firstWriteUtc, File.GetLastWriteTimeUtc(secondArchivePath));
        Assert.Equal(2, requestIndex);
        Assert.True(Directory.Exists(materializedCachePath));
        Assert.True(Directory.Exists(Path.Combine(materializedCachePath, "udx")));
        Assert.Equal(
            "stale",
            await File.ReadAllTextAsync(Path.Combine(materializedCachePath, "udx", "stale.dat")));
    }

    [Fact]
    public async Task ResolveAsyncFallsBackToExistingArchiveWhenMetadataIsMissingAndNetworkFails()
    {
        byte[] zipBytes = CreateZipArchive(("udx/bldg/533944/plateau_tokyo23ku_bldg_533944.gml", "<CityModel />"));
        using TemporaryDirectory workRoot = new();
        Uri archiveUri = new("https://example.test/533944.zip", UriKind.Absolute);
        string archiveHash = CreateArchiveCacheKey(archiveUri);
        string cacheRoot = Path.Combine(workRoot.Path, "cache", "remote", "tokyo23ku", archiveHash);
        Directory.CreateDirectory(cacheRoot);
        string archivePath = Path.Combine(cacheRoot, "533944.zip");
        await File.WriteAllBytesAsync(archivePath, zipBytes);

        using StubHttpMessageHandler handler = new(_ => throw new HttpRequestException("offline"));
        using HttpClient httpClient = new(handler);
        CkanPlateauDatasetSourceResolver resolver = new(httpClient);

        PlateauImportRequest resolvedRequest = await resolver.ResolveAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "533944",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: archiveUri),
            workRoot.Path);

        Assert.Equal(archivePath, resolvedRequest.LocalSourcePath);
        Assert.True(File.Exists(resolvedRequest.LocalSourcePath));
    }

    private static byte[] CreateZipArchive(params (string path, string content)[] entries)
    {
        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, true))
        {
            foreach ((string path, string content) in entries)
            {
                ZipArchiveEntry entry = zip.CreateEntry(path);
                using StreamWriter writer = new(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static string ReadZipEntry(string archivePath, string entryPath)
    {
        using FileStream archiveStream = File.OpenRead(archivePath);
        using ZipArchive zip = new(archiveStream, ZipArchiveMode.Read);
        ZipArchiveEntry? entry = zip.GetEntry(entryPath);
        Assert.NotNull(entry);

        using StreamReader reader = new(entry!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string CreateStaleMaterializedCacheDirectory(string workRoot, string localArchivePath)
    {
        string cacheName = GetMaterializedArchiveCacheKey(localArchivePath);
        string materializedCachePath = Path.Combine(workRoot, ".generated-assets", ".dataset-cache", cacheName);
        string staleFilePath = Path.Combine(materializedCachePath, "udx", "stale.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(staleFilePath)!);
        File.WriteAllText(staleFilePath, "stale");
        return materializedCachePath;
    }

    private static string GetMaterializedArchiveCacheKey(string archivePath)
    {
        string fullArchivePath = Path.GetFullPath(archivePath);
        string fileStem = Path.GetFileNameWithoutExtension(fullArchivePath);
        string digest = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(fullArchivePath)))
            .ToLowerInvariant();
        return $"{fileStem}-{digest[..12]}";
    }

    private static string CreateArchiveCacheKey(Uri archiveUri)
    {
        return Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(archiveUri.ToString())))
            .ToLowerInvariant();
    }

    private sealed class FailingHttpContent(byte[] content, int failAfterBytes) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            int writeLength = Math.Max(0, Math.Min(content.Length, failAfterBytes));
            await stream.WriteAsync(content.AsMemory(0, writeLength));
            throw new IOException("Injected failure during download stream serialization.");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = content.Length;
            return true;
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
