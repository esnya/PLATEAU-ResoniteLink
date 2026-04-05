using System.IO.Compression;
using System.Net;
using System.Text;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class CkanPlateauDatasetSourceResolverTests
{
    [Fact]
    public async Task ResolveAsyncDownloadsAndExtractsZipArchiveFromCkan()
    {
        byte[] nestedBuildingZipBytes = CreateZipArchive(
            ("plateau_tokyo23ku_bldg_533944.gml", "<CityModel />"),
            ("appearance/roof.png", "fake"));
        byte[] zipBytes = CreateZipArchive(
            ("bldg.zip", nestedBuildingZipBytes),
            ("dem.zip", CreateZipArchive(("dummy.gml", "<CityModel />"))));
        string packageSearchJson =
            """
            {
              "result": {
                "results": [
                  {
                    "name": "plateau-tokyo23ku-citygml-2020",
                    "title": "3D都市モデル（Project PLATEAU）東京都23区（CityGML 2020年度）",
                    "resources": [
                      {
                        "name": "533944",
                        "description": "CityGML",
                        "format": "ZIP",
                        "url": "https://example.test/533944.zip"
                      }
                    ]
                  }
                ]
              }
            }
            """;

        using TemporaryDirectory outputRoot = new();
        using StubHttpMessageHandler handler = new(request =>
        {
            if (request.RequestUri is null)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            if (request.RequestUri.AbsoluteUri.StartsWith("https://search.ckan.jp/backend/api/package_search", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(packageSearchJson, Encoding.UTF8, "application/json"),
                };
            }

            if (string.Equals(request.RequestUri.AbsoluteUri, "https://example.test/533944.zip", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(zipBytes),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using HttpClient httpClient = new(handler);

        CkanPlateauDatasetSourceResolver resolver = new(httpClient);

        PlateauImportRequest resolvedRequest = await resolver.ResolveAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "533944",
                SourceKind: DatasetSourceKind.Server,
                InputPath: null,
                ServerUri: null),
            outputRoot.Path);

        Assert.Equal(DatasetSourceKind.Local, resolvedRequest.SourceKind);
        Assert.NotNull(resolvedRequest.InputPath);
        Assert.True(Directory.Exists(resolvedRequest.InputPath));
        Assert.True(File.Exists(Path.Combine(
            resolvedRequest.InputPath,
            "udx",
            "bldg",
            "plateau_tokyo23ku_bldg_533944.gml")));
    }

    [Fact]
    public async Task ResolveAsyncAcceptsDirectArchiveUri()
    {
        byte[] zipBytes = CreateZipArchive(
            ("udx/bldg/533944/plateau_tokyo23ku_bldg_533944.gml", "<CityModel />"));

        using TemporaryDirectory outputRoot = new();
        using StubHttpMessageHandler handler = new(request =>
        {
            if (request.RequestUri is not null
                && string.Equals(request.RequestUri.AbsoluteUri, "https://example.test/direct.zip", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(zipBytes),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using HttpClient httpClient = new(handler);

        CkanPlateauDatasetSourceResolver resolver = new(httpClient);

        PlateauImportRequest resolvedRequest = await resolver.ResolveAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "533944",
                SourceKind: DatasetSourceKind.Server,
                InputPath: null,
                ServerUri: new Uri("https://example.test/direct.zip", UriKind.Absolute)),
            outputRoot.Path);

        Assert.Equal(DatasetSourceKind.Local, resolvedRequest.SourceKind);
        Assert.NotNull(resolvedRequest.InputPath);
        Assert.True(File.Exists(Path.Combine(
            resolvedRequest.InputPath,
            "udx",
            "bldg",
            "533944",
            "plateau_tokyo23ku_bldg_533944.gml")));
    }

    private static byte[] CreateZipArchive(params (string Path, string Content)[] entries)
    {
        using MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, string content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(path);
                using StreamWriter writer = new(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static byte[] CreateZipArchive(params (string Path, byte[] Content)[] entries)
    {
        using MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(path);
                using Stream entryStream = entry.Open();
                entryStream.Write(content, 0, content.Length);
            }
        }

        return stream.ToArray();
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(handler(request));
        }
    }
}
