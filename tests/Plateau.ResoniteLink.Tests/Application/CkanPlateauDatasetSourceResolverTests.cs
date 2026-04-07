using System.IO.Compression;
using System.Net;
using System.Text;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

using SharpCompress.Common;
using SharpCompress.Writers.SevenZip;

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

        using TemporaryDirectory workRoot = new();
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
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: null),
            workRoot.Path);

        Assert.Equal(DatasetSourceKind.Local, resolvedRequest.SourceKind);
        await AssertResolvedArchiveContainsAsync(
            resolvedRequest,
            "533944.zip",
            "udx/bldg/plateau_tokyo23ku_bldg_533944.gml");
    }

    [Fact]
    public async Task ResolveAsyncAcceptsDirectArchiveUri()
    {
        byte[] zipBytes = CreateZipArchive(
            ("udx/bldg/533944/plateau_tokyo23ku_bldg_533944.gml", "<CityModel />"));

        using TemporaryDirectory workRoot = new();
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
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri("https://example.test/direct.zip", UriKind.Absolute)),
            workRoot.Path);

        Assert.Equal(DatasetSourceKind.Local, resolvedRequest.SourceKind);
        await AssertResolvedArchiveContainsAsync(
            resolvedRequest,
            "direct.zip",
            "udx/bldg/533944/plateau_tokyo23ku_bldg_533944.gml");
    }

    [Fact]
    public async Task ResolveAsyncAcceptsDirectSevenZipArchiveUri()
    {
        byte[] archiveBytes = CreateSevenZipArchive(
            ("udx/bldg/533944/plateau_tokyo23ku_bldg_533944.gml", "<CityModel />"));

        using TemporaryDirectory workRoot = new();
        using StubHttpMessageHandler handler = new(request =>
        {
            if (request.RequestUri is not null
                && string.Equals(request.RequestUri.AbsoluteUri, "https://example.test/direct.7z", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(archiveBytes),
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
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri("https://example.test/direct.7z", UriKind.Absolute)),
            workRoot.Path);

        Assert.Equal(DatasetSourceKind.Local, resolvedRequest.SourceKind);
        await AssertResolvedArchiveContainsAsync(
            resolvedRequest,
            "direct.7z",
            "udx/bldg/533944/plateau_tokyo23ku_bldg_533944.gml");
    }

    [Fact]
    public async Task ResolveAsyncUsesNestedDatasetRootWhenArchiveWrapsContentsInTopLevelDirectory()
    {
        byte[] zipBytes = CreateZipArchive(
            ("13100_tokyo23-ku_2022_citygml_1_2_op/udx/bldg/53394611_bldg_6697_2_op.gml", "<CityModel />"));

        using TemporaryDirectory workRoot = new();
        using StubHttpMessageHandler handler = new(request =>
        {
            if (request.RequestUri is not null
                && string.Equals(request.RequestUri.AbsoluteUri, "https://example.test/wrapped.zip", StringComparison.Ordinal))
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
                MeshCode: "53394611",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri("https://example.test/wrapped.zip", UriKind.Absolute)),
            workRoot.Path);

        Assert.Equal(DatasetSourceKind.Local, resolvedRequest.SourceKind);
        await AssertResolvedArchiveContainsAsync(
            resolvedRequest,
            "wrapped.zip",
            "udx/bldg/53394611_bldg_6697_2_op.gml");
    }

    [Fact]
    public async Task ResolveAsyncUsesNestedDatasetRootWhenSevenZipArchiveWrapsContentsInTopLevelDirectory()
    {
        byte[] archiveBytes = CreateSevenZipArchive(
            ("13100_tokyo23-ku_2022_citygml_1_2_op/udx/bldg/53394611_bldg_6697_2_op.gml", "<CityModel />"));

        using TemporaryDirectory workRoot = new();
        using StubHttpMessageHandler handler = new(request =>
        {
            if (request.RequestUri is not null
                && string.Equals(request.RequestUri.AbsoluteUri, "https://example.test/wrapped.7z", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(archiveBytes),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using HttpClient httpClient = new(handler);

        CkanPlateauDatasetSourceResolver resolver = new(httpClient);

        PlateauImportRequest resolvedRequest = await resolver.ResolveAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394611",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri("https://example.test/wrapped.7z", UriKind.Absolute)),
            workRoot.Path);

        Assert.Equal(DatasetSourceKind.Local, resolvedRequest.SourceKind);
        await AssertResolvedArchiveContainsAsync(
            resolvedRequest,
            "wrapped.7z",
            "udx/bldg/53394611_bldg_6697_2_op.gml");
    }

    [Fact]
    public async Task ResolveAsyncExtractsOfficialPlateauPackageArchivesUnderUdx()
    {
        byte[] zipBytes = CreateZipArchive(
            ("area.zip", CreateZipArchive(("plateau_tokyo23ku_area_533944.gml", "<CityModel />"))),
            ("cons.zip", CreateZipArchive(("plateau_tokyo23ku_cons_533944.gml", "<CityModel />"))),
            ("ifld.zip", CreateZipArchive(("plateau_tokyo23ku_ifld_533944.gml", "<CityModel />"))),
            ("rfld.zip", CreateZipArchive(("plateau_tokyo23ku_rfld_533944.gml", "<CityModel />"))),
            ("rwy.zip", CreateZipArchive(("plateau_tokyo23ku_rwy_533944.gml", "<CityModel />"))),
            ("squr.zip", CreateZipArchive(("plateau_tokyo23ku_squr_533944.gml", "<CityModel />"))),
            ("tnm.zip", CreateZipArchive(("plateau_tokyo23ku_tnm_533944.gml", "<CityModel />"))),
            ("trk.zip", CreateZipArchive(("plateau_tokyo23ku_trk_533944.gml", "<CityModel />"))),
            ("ubld.zip", CreateZipArchive(("plateau_tokyo23ku_ubld_533944.gml", "<CityModel />"))),
            ("unf.zip", CreateZipArchive(("plateau_tokyo23ku_unf_533944.gml", "<CityModel />"))),
            ("urf.zip", CreateZipArchive(("plateau_tokyo23ku_urf_533944.gml", "<CityModel />"))),
            ("wtr.zip", CreateZipArchive(("plateau_tokyo23ku_wtr_533944.gml", "<CityModel />"))),
            ("wwy.zip", CreateZipArchive(("plateau_tokyo23ku_wwy_533944.gml", "<CityModel />"))));

        using TemporaryDirectory workRoot = new();
        using StubHttpMessageHandler handler = new(request =>
        {
            if (request.RequestUri is not null
                && string.Equals(request.RequestUri.AbsoluteUri, "https://example.test/official-packages.zip", StringComparison.Ordinal))
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
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri("https://example.test/official-packages.zip", UriKind.Absolute)),
            workRoot.Path);

        Assert.Equal(DatasetSourceKind.Local, resolvedRequest.SourceKind);
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.zip", "udx/area/plateau_tokyo23ku_area_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.zip", "udx/cons/plateau_tokyo23ku_cons_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.zip", "udx/ifld/plateau_tokyo23ku_ifld_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.zip", "udx/rfld/plateau_tokyo23ku_rfld_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.zip", "udx/rwy/plateau_tokyo23ku_rwy_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.zip", "udx/squr/plateau_tokyo23ku_squr_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.zip", "udx/tnm/plateau_tokyo23ku_tnm_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.zip", "udx/trk/plateau_tokyo23ku_trk_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.zip", "udx/ubld/plateau_tokyo23ku_ubld_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.zip", "udx/unf/plateau_tokyo23ku_unf_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.zip", "udx/urf/plateau_tokyo23ku_urf_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.zip", "udx/wtr/plateau_tokyo23ku_wtr_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.zip", "udx/wwy/plateau_tokyo23ku_wwy_533944.gml");
    }

    [Fact]
    public async Task ResolveAsyncExtractsOfficialPlateauPackageSevenZipArchivesUnderUdx()
    {
        byte[] archiveBytes = CreateSevenZipArchive(
            ("area.7z", CreateSevenZipArchive(("plateau_tokyo23ku_area_533944.gml", "<CityModel />"))),
            ("cons.7z", CreateSevenZipArchive(("plateau_tokyo23ku_cons_533944.gml", "<CityModel />"))),
            ("ifld.7z", CreateSevenZipArchive(("plateau_tokyo23ku_ifld_533944.gml", "<CityModel />"))),
            ("rfld.7z", CreateSevenZipArchive(("plateau_tokyo23ku_rfld_533944.gml", "<CityModel />"))),
            ("rwy.7z", CreateSevenZipArchive(("plateau_tokyo23ku_rwy_533944.gml", "<CityModel />"))),
            ("squr.7z", CreateSevenZipArchive(("plateau_tokyo23ku_squr_533944.gml", "<CityModel />"))),
            ("tnm.7z", CreateSevenZipArchive(("plateau_tokyo23ku_tnm_533944.gml", "<CityModel />"))),
            ("trk.7z", CreateSevenZipArchive(("plateau_tokyo23ku_trk_533944.gml", "<CityModel />"))),
            ("ubld.7z", CreateSevenZipArchive(("plateau_tokyo23ku_ubld_533944.gml", "<CityModel />"))),
            ("unf.7z", CreateSevenZipArchive(("plateau_tokyo23ku_unf_533944.gml", "<CityModel />"))),
            ("urf.7z", CreateSevenZipArchive(("plateau_tokyo23ku_urf_533944.gml", "<CityModel />"))),
            ("wtr.7z", CreateSevenZipArchive(("plateau_tokyo23ku_wtr_533944.gml", "<CityModel />"))),
            ("wwy.7z", CreateSevenZipArchive(("plateau_tokyo23ku_wwy_533944.gml", "<CityModel />"))));

        using TemporaryDirectory workRoot = new();
        using StubHttpMessageHandler handler = new(request =>
        {
            if (request.RequestUri is not null
                && string.Equals(request.RequestUri.AbsoluteUri, "https://example.test/official-packages.7z", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(archiveBytes),
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
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri("https://example.test/official-packages.7z", UriKind.Absolute)),
            workRoot.Path);

        Assert.Equal(DatasetSourceKind.Local, resolvedRequest.SourceKind);
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.7z", "udx/area/plateau_tokyo23ku_area_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.7z", "udx/cons/plateau_tokyo23ku_cons_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.7z", "udx/ifld/plateau_tokyo23ku_ifld_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.7z", "udx/rfld/plateau_tokyo23ku_rfld_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.7z", "udx/rwy/plateau_tokyo23ku_rwy_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.7z", "udx/squr/plateau_tokyo23ku_squr_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.7z", "udx/tnm/plateau_tokyo23ku_tnm_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.7z", "udx/trk/plateau_tokyo23ku_trk_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.7z", "udx/ubld/plateau_tokyo23ku_ubld_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.7z", "udx/unf/plateau_tokyo23ku_unf_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.7z", "udx/urf/plateau_tokyo23ku_urf_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.7z", "udx/wtr/plateau_tokyo23ku_wtr_533944.gml");
        await AssertResolvedArchiveContainsAsync(resolvedRequest, "official-packages.7z", "udx/wwy/plateau_tokyo23ku_wwy_533944.gml");
    }

    [Fact]
    public async Task ResolveAsyncDownloadsAndExtractsSevenZipArchiveFromCkan()
    {
        byte[] nestedBuildingArchiveBytes = CreateSevenZipArchive(
            ("plateau_tokyo23ku_bldg_533944.gml", "<CityModel />"),
            ("appearance/roof.png", "fake"));
        byte[] archiveBytes = CreateSevenZipArchive(
            ("bldg.7z", nestedBuildingArchiveBytes),
            ("dem.7z", CreateSevenZipArchive(("dummy.gml", "<CityModel />"))));
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
                        "format": "7Z",
                        "url": "https://example.test/533944.7z"
                      }
                    ]
                  }
                ]
              }
            }
            """;

        using TemporaryDirectory workRoot = new();
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

            if (string.Equals(request.RequestUri.AbsoluteUri, "https://example.test/533944.7z", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(archiveBytes),
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
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: null),
            workRoot.Path);

        Assert.Equal(DatasetSourceKind.Local, resolvedRequest.SourceKind);
        await AssertResolvedArchiveContainsAsync(
            resolvedRequest,
            "533944.7z",
            "udx/bldg/plateau_tokyo23ku_bldg_533944.gml");
    }

    [Fact]
    public async Task ResolveAsyncExtractsNestedMixedArchives()
    {
        byte[] sevenZipBytes = CreateSevenZipArchive(
            ("plateau_tokyo23ku_bldg_533944.gml", "<CityModel />"));
        byte[] zipBytes = CreateZipArchive(
            ("dummy.gml", "<CityModel />"));

        using TemporaryDirectory zipWorkRoot = new();
        using TemporaryDirectory sevenZipWorkRoot = new();

        await AssertArchiveExtractsNestedMixedArchivesAsync(
            CreateZipArchive(("bldg.7z", sevenZipBytes)),
            "https://example.test/mixed.zip",
            zipWorkRoot.Path,
            Path.Combine("udx", "bldg", "plateau_tokyo23ku_bldg_533944.gml"));

        await AssertArchiveExtractsNestedMixedArchivesAsync(
            CreateSevenZipArchive(("dem.zip", zipBytes)),
            "https://example.test/mixed.7z",
            sevenZipWorkRoot.Path,
            Path.Combine("udx", "dem", "dummy.gml"));
    }

    [Fact]
    public async Task ResolveAsyncRejectsUnsupportedDirectArchiveExtension()
    {
        using TemporaryDirectory workRoot = new();
        using StubHttpMessageHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using HttpClient httpClient = new(handler);

        CkanPlateauDatasetSourceResolver resolver = new(httpClient);

        PlateauImportValidationException exception = await Assert.ThrowsAsync<PlateauImportValidationException>(
            () => resolver.ResolveAsync(
                new PlateauImportRequest(
                    Dataset: "tokyo23ku",
                    MeshCode: "533944",
                    SourceKind: DatasetSourceKind.Remote,
                    LocalSourcePath: null,
                    ServerUri: new Uri("https://example.test/direct.rar", UriKind.Absolute)),
                workRoot.Path));

        Assert.Contains(
            exception.Errors,
            error => error.Contains("not a supported archive", StringComparison.Ordinal));
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

    private static byte[] CreateSevenZipArchive(params (string Path, string Content)[] entries)
    {
        using MemoryStream stream = new();
        using (SharpCompress.Writers.IWriter writer = SevenZipWriter.OpenWriter(
            stream,
            new SevenZipWriterOptions(CompressionType.LZMA)
            {
                LeaveStreamOpen = true,
            }))
        {
            foreach ((string path, string content) in entries)
            {
                using MemoryStream entryStream = new(Encoding.UTF8.GetBytes(content));
                writer.Write(path, entryStream, modificationTime: null);
            }
        }

        return stream.ToArray();
    }

    private static byte[] CreateSevenZipArchive(params (string Path, byte[] Content)[] entries)
    {
        using MemoryStream stream = new();
        using (SharpCompress.Writers.IWriter writer = SevenZipWriter.OpenWriter(
            stream,
            new SevenZipWriterOptions(CompressionType.LZMA)
            {
                LeaveStreamOpen = true,
            }))
        {
            foreach ((string path, byte[] content) in entries)
            {
                using MemoryStream entryStream = new(content, writable: false);
                writer.Write(path, entryStream, modificationTime: null);
            }
        }

        return stream.ToArray();
    }

    private static async Task AssertArchiveExtractsNestedMixedArchivesAsync(
        byte[] archiveBytes,
        string archiveUri,
        string workRoot,
        string expectedPathSuffix)
    {
        using StubHttpMessageHandler handler = new(request =>
        {
            if (request.RequestUri is not null
                && string.Equals(request.RequestUri.AbsoluteUri, archiveUri, StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(archiveBytes),
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
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri(archiveUri, UriKind.Absolute)),
            workRoot);

        await AssertResolvedArchiveContainsAsync(
            resolvedRequest,
            Path.GetFileName(new Uri(archiveUri, UriKind.Absolute).LocalPath),
            expectedPathSuffix.Replace('\\', '/'));
    }

    private static async Task AssertResolvedArchiveContainsAsync(
        PlateauImportRequest resolvedRequest,
        string expectedArchiveFileName,
        string expectedRelativePath)
    {
        Assert.NotNull(resolvedRequest.LocalSourcePath);
        Assert.True(File.Exists(resolvedRequest.LocalSourcePath));
        Assert.Equal(expectedArchiveFileName, Path.GetFileName(resolvedRequest.LocalSourcePath));

        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(resolvedRequest.LocalSourcePath);
        Assert.Contains(expectedRelativePath, datasetSource.EnumerateFiles());
        Assert.True(datasetSource.FileExists(expectedRelativePath));
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
