using System.IO.Compression;
using System.Net;
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
