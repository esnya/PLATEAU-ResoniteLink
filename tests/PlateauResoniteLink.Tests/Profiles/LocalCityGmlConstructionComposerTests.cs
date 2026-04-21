using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using GeographicLib;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class LocalCityGmlConstructionComposerTests
{
    [Fact]
    public void ComposeMapsDocumentSetBoundaryIntoImportedSceneMetadata()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "/tmp/plateau",
            ServerUri: null);

        TerrainTextureOverlay overlay = new(
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
            ["53394525"]);
        LocalCityGmlDocumentReadResult readResult = new(
            documentSet,
            new LocalCityGmlBootstrapContext([], new GeodeticPoint(35.0, 139.0, 12.5)));

        LocalCityGmlConstructionComposer composer = new(
            new ThrowingGeometryProjector(),
            new LocalCityGmlCommonMaterialEnumerator(new DefaultMaterialResolver()),
            new StubDemTextureSourcePolicy());

        IImportedSceneSource source = composer.Compose(request, readResult);

        Assert.Equal("3.0", source.Metadata.SchemaVersion);
        Assert.Equal("PLATEAU tokyo23ku 53394525", source.Metadata.SceneName);
        Assert.Same(request, source.Metadata.Request);
        Assert.Equal(documentSet.PackageNames, source.Metadata.SourceDataset.PackageNames);
        Assert.Equal(documentSet.RelativeSourceFiles, source.Metadata.SourceDataset.SourceFiles);
        Assert.Equal(documentSet.SelectedMeshCodes, source.Metadata.SourceDataset.RequestedMeshCodes);
        Assert.Equal(35.0, source.Metadata.GeodeticOrigin.Latitude);
        Assert.Equal(139.0, source.Metadata.GeodeticOrigin.Longitude);
        Assert.Equal(12.5, source.Metadata.GeodeticOrigin.Altitude);
    }

    private sealed class ThrowingGeometryProjector : ICityGmlGeometryProjector
    {
        public IEnumerable<ResoniteConstructionCityObject> ProjectCityObjects(
            CachedSourceFileDescriptor sourceFile,
            CoordinateReferenceSystem referenceSystem,
            GeodeticPoint globalOriginPoint,
            LocalCartesian? globalCartesian,
            IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
            IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
            PlateauImportRequest request,
            Func<BootstrapParsedCityObject, bool>? predicate = null)
        {
            _ = sourceFile;
            _ = referenceSystem;
            _ = globalOriginPoint;
            _ = globalCartesian;
            _ = demTerrainTextureOverlays;
            _ = requestedMeshAreas;
            _ = request;
            _ = predicate;
            throw new InvalidOperationException("Compose should not project geometry.");
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

        public Task<string> EnsureLocalFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            throw new FileNotFoundException(relativePath);
        }
    }

    private sealed class StubDemTextureSourcePolicy : IDemTextureSourcePolicy
    {
        public Task<ResolvedDemTextureSources> ResolveAsync(
            PlateauImportRequest request,
            IReadOnlyList<string> requestedMeshCodes,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = requestedMeshCodes;
            return Task.FromResult(new ResolvedDemTextureSources([]));
        }

        public IReadOnlyList<TerrainTextureOverlay> CreateMapTileFallbackOverlays(
            IReadOnlyList<DemTerrainOverlayRegion> overlayRegions)
        {
            _ = overlayRegions;
            return [];
        }
    }
}
