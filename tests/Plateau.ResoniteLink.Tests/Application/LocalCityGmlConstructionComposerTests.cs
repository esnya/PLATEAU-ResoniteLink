using GeographicLib;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class LocalCityGmlConstructionComposerTests
{
    [Fact]
    public void ComposeMapsDocumentSetBoundaryIntoConstructionMetadata()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "/tmp/plateau",
            ServerUri: null);

        TerrainTextureOverlay overlay = new(
            TexturePath: "appearance/overlay.png",
            PackageName: "bldg",
            UrlTemplate: "https://example.invalid/{z}/{x}/{y}.png",
            ZoomLevel: 14,
            GeographicBounds: new GeographicRectangle(35.0, 35.1, 139.0, 139.1),
            MaxTextureSize: 1024);

        LocalCityGmlDocumentSet documentSet = new(
            new EmptyDatasetContentSource(),
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"],
            ["bldg", "dem"],
            [overlay],
            ["53394525"],
            [],
            [],
            CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697").ToLegacy(),
            new GeodeticPoint(35.0, 139.0, 12.5).ToLegacy(),
            terrainHeightSampler: null);

        LocalCityGmlConstructionComposer composer = new(new ThrowingGeometryProjector());

        IResoniteConstructionSource source = composer.Compose(request, documentSet);

        Assert.Equal("3.0", source.Metadata.SchemaVersion);
        Assert.Equal("PLATEAU tokyo23ku 53394525", source.Metadata.WorldName);
        Assert.Same(request, source.Metadata.Request);
        Assert.Equal(documentSet.PackageNames, source.Metadata.SourceDataset.PackageNames);
        Assert.Equal(documentSet.RelativeSourceFiles, source.Metadata.SourceDataset.SourceFiles);
        Assert.Equal(documentSet.TerrainTextureOverlays, source.Metadata.SourceDataset.TerrainTextureOverlays);
        Assert.Equal(documentSet.RequestedMeshCodes, source.Metadata.SourceDataset.RequestedMeshCodes);
        Assert.Equal(documentSet.GlobalOriginPoint.Latitude, source.Metadata.LocalOrigin.Latitude);
        Assert.Equal(documentSet.GlobalOriginPoint.Longitude, source.Metadata.LocalOrigin.Longitude);
        Assert.Equal(documentSet.GlobalOriginPoint.Altitude, source.Metadata.LocalOrigin.Altitude);
    }

    private sealed class ThrowingGeometryProjector : ICityGmlGeometryProjector
    {
        public IEnumerable<ResoniteConstructionCityObject> MaterializeCityObjects(
            CachedSourceFileDescriptor sourceFile,
            CoordinateReferenceSystem referenceSystem,
            GeodeticPoint globalOriginPoint,
            LocalCartesian? globalCartesian,
            IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
            TerrainHeightSampler? terrainHeightSampler,
            PlateauImportRequest request,
            Func<BootstrapParsedCityObject, bool>? predicate = null)
        {
            throw new InvalidOperationException("Compose should not materialize geometry.");
        }
    }

    private sealed class EmptyDatasetContentSource : IPlateauDatasetContentSource
    {
        public string SourcePath => "/tmp/plateau";

        public IReadOnlyList<string> EnumerateFiles()
        {
            return [];
        }

        public bool FileExists(string relativePath)
        {
            return false;
        }

        public ValueTask<Stream> OpenReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            throw new FileNotFoundException(relativePath);
        }

        public Task<string> MaterializeFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            throw new FileNotFoundException(relativePath);
        }
    }
}
