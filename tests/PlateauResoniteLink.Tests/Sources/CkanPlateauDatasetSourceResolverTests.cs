using PlateauResoniteLink.Application.Importing;

using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

using SharpCompress.Common;
using SharpCompress.Writers.SevenZip;

namespace PlateauResoniteLink.Tests.Sources;

[Trait("Category", "Slow")]
public sealed class CkanPlateauDatasetSourceResolverTests
{
    private static CkanPlateauDatasetSourceResolver CreateResolver(HttpClient httpClient)
    {
        return new CkanPlateauDatasetSourceResolver(
            httpClient,
            new RemoteArchiveDistributionPolicy(),
            new ArchiveFileLayoutPolicy());
    }

    private static ValidatedPlateauImportRequest CreateValidatedRemoteRequest(
        string dataset,
        string meshCode,
        string serverUri)
    {
        return PlateauImportRequestValidator.NormalizeAndValidateOrThrow(
            new PlateauImportRequest(
                Dataset: dataset,
                MeshCode: meshCode,
                CityGmlSource: DatasetLocation.Remote(new Uri(serverUri, UriKind.Absolute))));
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

        CkanPlateauDatasetSourceResolver resolver = CreateResolver(httpClient);

        ResolvedLocalPlateauImportRequest resolvedRequest = await resolver.ResolveAsync(
            CreateValidatedRemoteRequest("tokyo23ku", "533944", "https://example.test/direct.zip"),
            workRoot.Path);

        Assert.False(string.IsNullOrWhiteSpace(resolvedRequest.CityGmlLocalSourcePath));
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

        CkanPlateauDatasetSourceResolver resolver = CreateResolver(httpClient);

        ResolvedLocalPlateauImportRequest resolvedRequest = await resolver.ResolveAsync(
            CreateValidatedRemoteRequest("tokyo23ku", "533944", "https://example.test/direct.7z"),
            workRoot.Path);

        Assert.False(string.IsNullOrWhiteSpace(resolvedRequest.CityGmlLocalSourcePath));
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

        CkanPlateauDatasetSourceResolver resolver = CreateResolver(httpClient);

        ResolvedLocalPlateauImportRequest resolvedRequest = await resolver.ResolveAsync(
            CreateValidatedRemoteRequest("tokyo23ku", "53394611", "https://example.test/wrapped.zip"),
            workRoot.Path);

        Assert.False(string.IsNullOrWhiteSpace(resolvedRequest.CityGmlLocalSourcePath));
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

        CkanPlateauDatasetSourceResolver resolver = CreateResolver(httpClient);

        ResolvedLocalPlateauImportRequest resolvedRequest = await resolver.ResolveAsync(
            CreateValidatedRemoteRequest("tokyo23ku", "53394611", "https://example.test/wrapped.7z"),
            workRoot.Path);

        Assert.False(string.IsNullOrWhiteSpace(resolvedRequest.CityGmlLocalSourcePath));
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

        CkanPlateauDatasetSourceResolver resolver = CreateResolver(httpClient);

        ResolvedLocalPlateauImportRequest resolvedRequest = await resolver.ResolveAsync(
            CreateValidatedRemoteRequest("tokyo23ku", "533944", "https://example.test/official-packages.zip"),
            workRoot.Path);

        Assert.False(string.IsNullOrWhiteSpace(resolvedRequest.CityGmlLocalSourcePath));
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

        CkanPlateauDatasetSourceResolver resolver = CreateResolver(httpClient);

        ResolvedLocalPlateauImportRequest resolvedRequest = await resolver.ResolveAsync(
            CreateValidatedRemoteRequest("tokyo23ku", "533944", "https://example.test/official-packages.7z"),
            workRoot.Path);

        Assert.False(string.IsNullOrWhiteSpace(resolvedRequest.CityGmlLocalSourcePath));
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
    public void NormalizeAndValidateOrThrowRejectsNonArchiveRemoteUrl()
    {
        PlateauImportValidationException exception = Assert.Throws<PlateauImportValidationException>(
            () => CreateValidatedRemoteRequest("tokyo23ku", "533944", "https://example.test/dataset-page"));

        Assert.Contains(
            exception.Errors,
            error => error.Contains(
                "must point directly to a .zip or .7z CityGML archive",
                StringComparison.Ordinal));
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
    public void NormalizeAndValidateOrThrowRejectsUnsupportedDirectArchiveExtension()
    {
        PlateauImportValidationException exception = Assert.Throws<PlateauImportValidationException>(
            () => CreateValidatedRemoteRequest("tokyo23ku", "533944", "https://example.test/direct.rar"));

        Assert.Contains(
            exception.Errors,
            error => error.Contains(
                "must point directly to a .zip or .7z CityGML archive",
                StringComparison.Ordinal));
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

        CkanPlateauDatasetSourceResolver resolver = CreateResolver(httpClient);

        ResolvedLocalPlateauImportRequest resolvedRequest = await resolver.ResolveAsync(
            CreateValidatedRemoteRequest("tokyo23ku", "533944", archiveUri),
            workRoot);

        await AssertResolvedArchiveContainsAsync(
            resolvedRequest,
            Path.GetFileName(new Uri(archiveUri, UriKind.Absolute).LocalPath),
            expectedPathSuffix.Replace('\\', '/'));
    }

    private static async Task AssertResolvedArchiveContainsAsync(
        ResolvedLocalPlateauImportRequest resolvedRequest,
        string expectedArchiveFileName,
        string expectedRelativePath)
    {
        Assert.NotNull(resolvedRequest.CityGmlLocalSourcePath);
        Assert.True(File.Exists(resolvedRequest.CityGmlLocalSourcePath));
        Assert.Equal(
            Path.GetExtension(expectedArchiveFileName).ToLowerInvariant(),
            Path.GetExtension(resolvedRequest.CityGmlLocalSourcePath));
        Assert.StartsWith(
            "source-archive-",
            Path.GetFileNameWithoutExtension(resolvedRequest.CityGmlLocalSourcePath),
            StringComparison.Ordinal);

        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(
            resolvedRequest.CityGmlLocalSourcePath,
            new RemoteArchiveDistributionPolicy(),
            new ArchiveFileLayoutPolicy());
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
