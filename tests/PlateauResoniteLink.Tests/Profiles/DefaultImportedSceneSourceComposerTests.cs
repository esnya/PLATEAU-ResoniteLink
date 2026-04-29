using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using GeographicLib;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Tests.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class DefaultImportedSceneSourceComposerTests
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

        ImportedSceneSourceDataset documentSet = new(
            new EmptyDatasetContentSource(),
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"],
            ["bldg", "dem"],
            [overlay],
            ["53394525"]);
        ImportedSceneSourceSnapshot readResult = new(
            documentSet,
            new ImportedSceneSourceContext([], new GeodeticPoint(35.0, 139.0, 12.5)));

        DefaultImportedSceneSourceComposer composer = new(
            new ThrowingGeometryProjector(),
            new StubDemTextureSourcePolicy());

        IImportedSceneSource source = composer.Compose(
            request,
            readResult,
            new PassthroughImportedObjectUnitOptimizer());

        Assert.Equal("3.0", source.Metadata.SchemaVersion);
        Assert.Equal("PLATEAU tokyo23ku 53394525", source.Metadata.SceneName);
        Assert.Same(request, source.Metadata.Request);
        Assert.Equal(documentSet.PackageNames, source.Metadata.SourceDataset.PackageNames);
        Assert.Equal(documentSet.RelativeSourceFiles, source.Metadata.SourceDataset.SourceFiles);
        Assert.Equal(documentSet.SelectedMeshCodes, source.Metadata.SourceDataset.SelectedMeshCodes);
        Assert.Equal(35.0, source.Metadata.GeodeticOrigin.Latitude);
        Assert.Equal(139.0, source.Metadata.GeodeticOrigin.Longitude);
        Assert.Equal(12.5, source.Metadata.GeodeticOrigin.Altitude);
        Assert.Equal(
            new LicenseMetadata(
                RequireCredit: true,
                CreditText: "Contains PLATEAU dataset content for tokyo23ku. Follow the original PLATEAU dataset terms and provide source attribution when redistributing derived content.",
                LicenseName: "PLATEAU Open Data Terms",
                LicenseUrl: "https://www.mlit.go.jp/plateau/site-policy/"),
            source.Metadata.Attribution.DatasetLicense);
        Assert.Empty(source.Metadata.Attribution.MaterialLicenses);
    }

    private sealed class ThrowingGeometryProjector : ICityGmlGeometryProjector
    {
        public IEnumerable<ImportedCityObject> ProjectCityObjects(
            CachedSourceFileDescriptor sourceFile,
            CoordinateReferenceSystem referenceSystem,
            GeodeticPoint globalOriginPoint,
            LocalCartesian? globalCartesian,
            IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
            IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
            PlateauImportRequest request,
            Func<ParsedCityObject, bool>? predicate = null,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            _ = sourceFile;
            _ = referenceSystem;
            _ = globalOriginPoint;
            _ = globalCartesian;
            _ = demTerrainTextureOverlays;
            _ = requestedMeshAreas;
            _ = request;
            _ = predicate;
            _ = progressReporter;
            _ = cancellationToken;
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

        public string? ResolveRelativePath(string baseRelativePath, string candidatePath)
        {
            return null;
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
            IReadOnlyList<DemTerrainOverlayRegion> overlayRegions,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = overlayRegions;
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
