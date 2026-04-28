using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
    public async Task GetStatsAsyncEstimatesArchiveDerivedTextureAndGeometryVram()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetFile(
            datasetRoot.Path,
            "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            """
            <core:CityModel
              xmlns:app="http://www.opengis.net/citygml/appearance/2.0"
              xmlns:bldg="http://www.opengis.net/citygml/building/2.0"
              xmlns:core="http://www.opengis.net/citygml/2.0"
              xmlns:gml="http://www.opengis.net/gml">
              <app:appearanceMember>
                <app:Appearance>
                  <app:surfaceDataMember>
                    <app:ParameterizedTexture>
                      <app:imageURI>appearance/opaque.png</app:imageURI>
                    </app:ParameterizedTexture>
                  </app:surfaceDataMember>
                  <app:surfaceDataMember>
                    <app:ParameterizedTexture>
                      <app:imageURI>appearance/cutout.png</app:imageURI>
                    </app:ParameterizedTexture>
                  </app:surfaceDataMember>
                </app:Appearance>
              </app:appearanceMember>
              <core:cityObjectMember>
                <bldg:Building>
                  <bldg:lod2MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon>
                          <gml:exterior>
                            <gml:LinearRing>
                              <gml:posList>0 0 0 1 0 0 1 1 0 0 1 0 0 0 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod2MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
            </core:CityModel>
            """);
        await WriteDatasetImageAsync(
            datasetRoot.Path,
            "udx/bldg/53394525/appearance/opaque.png",
            new Rgba32(255, 255, 255, 255));
        await WriteDatasetImageAsync(
            datasetRoot.Path,
            "udx/bldg/53394525/appearance/cutout.png",
            new Rgba32(255, 255, 255, 128));

        DatasetStatsResult result = await service.GetStatsAsync(datasetRoot.Path, ["bldg"]);

        Assert.Equal(2, result.ArchiveVramEstimate.RendererTextureVram.ResolvedTextureFileCount);
        Assert.Equal(1, result.ArchiveVramEstimate.RendererTextureVram.Bc1TextureCount);
        Assert.Equal(1, result.ArchiveVramEstimate.RendererTextureVram.Bc3TextureCount);
        Assert.Equal(24, result.ArchiveVramEstimate.RendererTextureVram.Bc1Bytes);
        Assert.Equal(48, result.ArchiveVramEstimate.RendererTextureVram.Bc3Bytes);
        Assert.Equal(72, result.ArchiveVramEstimate.RendererTextureVram.RendererTotalBytes);
        Assert.Equal(128, result.ArchiveVramEstimate.RendererTextureVram.Rgba32PayloadBytes);
        Assert.Equal(4, result.ArchiveVramEstimate.RendererGeometryVram.PositionCount);
        Assert.Equal(2, result.ArchiveVramEstimate.RendererGeometryVram.TriangleCount);
        Assert.Equal(152, result.ArchiveVramEstimate.RendererGeometryVram.RendererBytesMin);
        Assert.Equal(280, result.ArchiveVramEstimate.RendererGeometryVram.RendererBytesMax);
        Assert.Equal(224, result.ArchiveVramEstimate.RendererTotalBytesMin);
        Assert.Equal(352, result.ArchiveVramEstimate.RendererTotalBytesMax);
    }

    [Fact]
    public async Task GetStatsAsyncEstimatesVramInsideArchiveSource()
    {
        byte[] archiveBytes = CreateZipArchive(
            (
                "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
                Encoding.UTF8.GetBytes(
                    """
                    <core:CityModel
                      xmlns:app="http://www.opengis.net/citygml/appearance/2.0"
                      xmlns:bldg="http://www.opengis.net/citygml/building/2.0"
                      xmlns:core="http://www.opengis.net/citygml/2.0"
                      xmlns:gml="http://www.opengis.net/gml">
                      <app:appearanceMember>
                        <app:Appearance>
                          <app:surfaceDataMember>
                            <app:ParameterizedTexture>
                              <app:imageURI>appearance/opaque.png</app:imageURI>
                            </app:ParameterizedTexture>
                          </app:surfaceDataMember>
                        </app:Appearance>
                      </app:appearanceMember>
                      <core:cityObjectMember>
                        <bldg:Building>
                          <bldg:lod2MultiSurface>
                            <gml:MultiSurface>
                              <gml:surfaceMember>
                                <gml:Polygon>
                                  <gml:exterior>
                                    <gml:LinearRing>
                                      <gml:posList>0 0 0 1 0 0 1 1 0 0 0 0</gml:posList>
                                    </gml:LinearRing>
                                  </gml:exterior>
                                </gml:Polygon>
                              </gml:surfaceMember>
                            </gml:MultiSurface>
                          </bldg:lod2MultiSurface>
                        </bldg:Building>
                      </core:cityObjectMember>
                    </core:CityModel>
                    """)),
            ("udx/bldg/53394525/appearance/opaque.png", await CreatePngBytesAsync(new Rgba32(255, 255, 255, 255))));
        using TemporaryDirectory workRoot = new();
        string archivePath = Path.Combine(workRoot.Path, "dataset.zip");
        await File.WriteAllBytesAsync(archivePath, archiveBytes);

        DatasetStatsResult result = await service.GetStatsAsync(archivePath, ["bldg"]);

        Assert.Equal(1, result.ArchiveVramEstimate.RendererTextureVram.ResolvedTextureFileCount);
        Assert.Equal(1, result.ArchiveVramEstimate.RendererTextureVram.Bc1TextureCount);
        Assert.Equal(24, result.ArchiveVramEstimate.RendererTextureVram.RendererTotalBytes);
        Assert.Equal(3, result.ArchiveVramEstimate.RendererGeometryVram.PositionCount);
        Assert.Equal(1, result.ArchiveVramEstimate.RendererGeometryVram.TriangleCount);
    }

    [Fact]
    public async Task GetStatsAsyncPreservesNonClosedPosListVerticesInGeometryVramEstimate()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetFile(
            datasetRoot.Path,
            "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            """
            <core:CityModel
              xmlns:bldg="http://www.opengis.net/citygml/building/2.0"
              xmlns:core="http://www.opengis.net/citygml/2.0"
              xmlns:gml="http://www.opengis.net/gml">
              <core:cityObjectMember>
                <bldg:Building>
                  <bldg:lod2MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon>
                          <gml:exterior>
                            <gml:LinearRing>
                              <gml:posList>0 0 0 1 0 0 0 1 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod2MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
            </core:CityModel>
            """);

        DatasetStatsResult result = await service.GetStatsAsync(datasetRoot.Path, ["bldg"]);

        Assert.Equal(3, result.ArchiveVramEstimate.RendererGeometryVram.PositionCount);
        Assert.Equal(1, result.ArchiveVramEstimate.RendererGeometryVram.TriangleCount);
    }

    [Fact]
    public async Task GetStatsAsyncIgnoresTrailingPosListRemainderLikeImporterParser()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetFile(
            datasetRoot.Path,
            "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            """
            <core:CityModel
              xmlns:bldg="http://www.opengis.net/citygml/building/2.0"
              xmlns:core="http://www.opengis.net/citygml/2.0"
              xmlns:gml="http://www.opengis.net/gml">
              <core:cityObjectMember>
                <bldg:Building>
                  <bldg:lod2MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon>
                          <gml:exterior>
                            <gml:LinearRing>
                              <gml:posList>0 0 0 1 0 0 0 1 0 42</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod2MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
            </core:CityModel>
            """);

        DatasetStatsResult result = await service.GetStatsAsync(datasetRoot.Path, ["bldg"]);

        Assert.Equal(3, result.ArchiveVramEstimate.RendererGeometryVram.PositionCount);
        Assert.Equal(1, result.ArchiveVramEstimate.RendererGeometryVram.TriangleCount);
    }

    [Fact]
    public async Task GetStatsAsyncEstimatesGeometryVramFromGmlPosFallback()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetFile(
            datasetRoot.Path,
            "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            """
            <core:CityModel
              xmlns:bldg="http://www.opengis.net/citygml/building/2.0"
              xmlns:core="http://www.opengis.net/citygml/2.0"
              xmlns:gml="http://www.opengis.net/gml">
              <core:cityObjectMember>
                <bldg:Building>
                  <bldg:lod2MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon>
                          <gml:exterior>
                            <gml:LinearRing>
                              <gml:pos>0 0 0</gml:pos>
                              <gml:pos>1 0 0</gml:pos>
                              <gml:pos>0 1 0</gml:pos>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod2MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
            </core:CityModel>
            """);

        DatasetStatsResult result = await service.GetStatsAsync(datasetRoot.Path, ["bldg"]);

        Assert.Equal(3, result.ArchiveVramEstimate.RendererGeometryVram.PositionCount);
        Assert.Equal(1, result.ArchiveVramEstimate.RendererGeometryVram.TriangleCount);
    }

    [Fact]
    public async Task GetStatsAsyncIgnoresNonRingPosListForGeometryVramEstimate()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetFile(
            datasetRoot.Path,
            "udx/tran/53394525/plateau_tokyo23ku_tran_53394525.gml",
            """
            <core:CityModel
              xmlns:core="http://www.opengis.net/citygml/2.0"
              xmlns:gml="http://www.opengis.net/gml"
              xmlns:tran="http://www.opengis.net/citygml/transportation/2.0">
              <core:cityObjectMember>
                <tran:Road>
                  <tran:lod1MultiCurve>
                    <gml:MultiCurve>
                      <gml:curveMember>
                        <gml:LineString>
                          <gml:posList>0 0 0 1 0 0 2 0 0 3 0 0</gml:posList>
                        </gml:LineString>
                      </gml:curveMember>
                    </gml:MultiCurve>
                  </tran:lod1MultiCurve>
                </tran:Road>
              </core:cityObjectMember>
            </core:CityModel>
            """);

        DatasetStatsResult result = await service.GetStatsAsync(datasetRoot.Path, ["tran"]);

        Assert.Equal(0, result.ArchiveVramEstimate.RendererGeometryVram.PositionCount);
        Assert.Equal(0, result.ArchiveVramEstimate.RendererGeometryVram.TriangleCount);
    }

    [Fact]
    public async Task GetStatsAsyncUsesImporterThreeOrdinateGroupingForGeometryVramEstimate()
    {
        using TemporaryDirectory datasetRoot = new();
        WriteDatasetFile(
            datasetRoot.Path,
            "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            """
            <core:CityModel
              xmlns:bldg="http://www.opengis.net/citygml/building/2.0"
              xmlns:core="http://www.opengis.net/citygml/2.0"
              xmlns:gml="http://www.opengis.net/gml">
              <core:cityObjectMember>
                <bldg:Building>
                  <bldg:lod2MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon>
                          <gml:exterior>
                            <gml:LinearRing>
                              <gml:posList srsDimension="2">0 0 1 2 0 1</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod2MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
            </core:CityModel>
            """);

        DatasetStatsResult result = await service.GetStatsAsync(datasetRoot.Path, ["bldg"]);

        Assert.Equal(2, result.ArchiveVramEstimate.RendererGeometryVram.PositionCount);
        Assert.Equal(0, result.ArchiveVramEstimate.RendererGeometryVram.TriangleCount);
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

        Assert.Equal(["53394525", "53394526"], result.SelectedMeshCodes);
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

    private static async Task WriteDatasetImageAsync(string datasetRoot, string relativePath, Rgba32 color)
    {
        string absolutePath = Path.Combine(
            datasetRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        using Image<Rgba32> image = new(4, 4, color);
        await image.SaveAsPngAsync(absolutePath);
    }

    private static async Task<byte[]> CreatePngBytesAsync(Rgba32 color)
    {
        await using MemoryStream stream = new();
        using Image<Rgba32> image = new(4, 4, color);
        await image.SaveAsPngAsync(stream);
        return stream.ToArray();
    }

    private static byte[] CreateZipArchive(params (string Path, string Content)[] entries)
    {
        return CreateZipArchive(entries.Select(static entry => (entry.Path, Encoding.UTF8.GetBytes(entry.Content))).ToArray());
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
                entryStream.Write(content);
            }
        }

        return stream.ToArray();
    }
}
