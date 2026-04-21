using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Application;

public sealed class DatasetInspectionServiceTests
{
    private readonly DatasetInspectionService service = new(
        new DefaultPlateauDatasetContentSourceFactory(
            new RemoteArchiveDistributionPolicy(),
            new ArchiveFileLayoutPolicy()));

    [Fact]
    public async Task GetStatsAsyncSummarizesPackagesMeshCodesAndLods()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetFile(
            datasetRoot.Path,
            "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            "<bldg:CityModel xmlns:bldg=\"http://www.opengis.net/citygml/building/2.0\"><bldg:lod1Solid /><bldg:lod2Solid /></bldg:CityModel>");
        WriteDatasetFile(
            datasetRoot.Path,
            "udx/bldg/53394526/plateau_tokyo23ku_bldg_53394526.gml",
            "<bldg:CityModel xmlns:bldg=\"urn:test\" />");
        WriteDatasetFile(
            datasetRoot.Path,
            "udx/tran/53394525/plateau_tokyo23ku_tran_53394525.gml",
            "<tran:CityModel xmlns:tran=\"http://www.opengis.net/citygml/transportation/2.0\"><tran:lod1MultiSurface /></tran:CityModel>");

        DatasetStatsResult result = await service.GetStatsAsync(datasetRoot.Path, ["bldg", "tran"]);

        Assert.Equal(3, result.RecognizedSourceFileCount);
        Assert.Equal(2, result.PackageCounts["bldg"]);
        Assert.Equal(1, result.PackageCounts["tran"]);
        Assert.Equal(2, result.MeshCodeCounts["53394525"]);
        Assert.Equal(1, result.MeshCodeCounts["53394526"]);
        Assert.Equal(2, result.LodCoverageCounts[1]);
        Assert.Equal(1, result.LodCoverageCounts[2]);
        Assert.Equal(1, result.FilesWithoutDetectedLod);
    }

    [Fact]
    public async Task GetStatsAsyncIgnoresNonStructuralLodTokensInTextAttributesAndComments()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetFile(
            datasetRoot.Path,
            "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            """
            <bldg:CityModel xmlns:bldg="http://www.opengis.net/citygml/building/2.0" xmlns:gml="urn:gml" note="lod2 should not count">
              <!-- lod1 should not count -->
              <bldg:Building gml:id="lod3-building">
                <bldg:stringAttribute>lod4 should not count</bldg:stringAttribute>
              </bldg:Building>
            </bldg:CityModel>
            """);

        DatasetStatsResult result = await service.GetStatsAsync(datasetRoot.Path, ["bldg"]);

        Assert.Empty(result.LodCoverageCounts);
        Assert.Equal(1, result.FilesWithoutDetectedLod);
    }

    [Fact]
    public async Task GetStatsAsyncIgnoresForeignStructuralElementsThatOnlyLookLikeLodTags()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetFile(
            datasetRoot.Path,
            "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            """
            <bldg:CityModel xmlns:bldg="http://www.opengis.net/citygml/building/2.0" xmlns:foo="urn:foo">
              <foo:lod2Metadata />
            </bldg:CityModel>
            """);

        DatasetStatsResult result = await service.GetStatsAsync(datasetRoot.Path, ["bldg"]);

        Assert.Empty(result.LodCoverageCounts);
        Assert.Equal(1, result.FilesWithoutDetectedLod);
    }

    [Fact]
    public async Task GetStatsAsyncTreatsMalformedXmlAsNoDetectedLod()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetFile(
            datasetRoot.Path,
            "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            "<bldg:CityModel><bldg:lod2Solid></bldg:CityModel>");

        DatasetStatsResult result = await service.GetStatsAsync(datasetRoot.Path, ["bldg"]);

        Assert.Empty(result.LodCoverageCounts);
        Assert.Equal(1, result.FilesWithoutDetectedLod);
    }

    [Fact]
    public async Task GetStatsAsyncWrapsInvalidRequestedPackageNamesAsValidationFailure()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetFile(
            datasetRoot.Path,
            "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            "<bldg:CityModel xmlns:bldg=\"http://www.opengis.net/citygml/building/2.0\" />");

        PlateauImportValidationException exception = await Assert.ThrowsAsync<PlateauImportValidationException>(
            () => service.GetStatsAsync(datasetRoot.Path, ["not-a-package"]));

        Assert.Contains(
            exception.Errors,
            static error => error.Contains("Unsupported package", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsyncFindsMatchingFilesInsideArchiveSource()
    {
        byte[] archiveBytes = CreateZipArchive(
            ("udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml", "<bldg:CityModel />"),
            ("udx/bldg/53394526/plateau_tokyo23ku_bldg_53394526.gml", "<bldg:CityModel />"),
            ("udx/tran/53394525/plateau_tokyo23ku_tran_53394525.gml", "<tran:CityModel />"));
        using TemporaryDirectory workRoot = new();
        string archivePath = Path.Combine(workRoot.Path, "dataset.zip");
        await File.WriteAllBytesAsync(archivePath, archiveBytes);

        DatasetSearchResult result = await service.SearchAsync(archivePath, "5339452[56]", ["bldg"]);

        Assert.Equal(["53394525", "53394526"], result.RequestedMeshCodes);
        Assert.Equal(
            [
                "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
                "udx/bldg/53394526/plateau_tokyo23ku_bldg_53394526.gml",
            ],
            result.SourceFiles.Select(static entry => entry.RelativePath).ToArray());
    }

    private static void WriteDatasetFile(string datasetRoot, string relativePath, string content)
    {
        string absolutePath = Path.Combine(
            datasetRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        File.WriteAllText(absolutePath, content);
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
}
