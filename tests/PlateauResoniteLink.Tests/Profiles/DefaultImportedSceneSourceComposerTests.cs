using PlateauResoniteLink.Core.Application.Importing;
using PlateauResoniteLink.Plateau.Application.Importing;
using PlateauResoniteLink.Plateau.Application.Importing.CityGml;
using PlateauResoniteLink.Core.Application.Importing.Contracts;
using PlateauResoniteLink.Plateau.Application.Importing.Plateau;
using PlateauResoniteLink.Plateau.Application.Importing.Source;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using GeographicLib;


using PlateauResoniteLink.Core.Domain.Importing;
using PlateauResoniteLink.Tests.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class DefaultImportedSceneSourceComposerTests
{
    [Fact]
    public void ComposeMapsDocumentSetBoundaryIntoImportedSceneMetadata()
    {
        ResolvedLocalPlateauImportRequest request = ResolvedLocalPlateauImportRequestTestFactory.Create();
        PlateauImportRequest importRequest = request.ToImportRequest();

        TerrainTextureOverlay overlay = new(
            PackageName: "bldg",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            UrlTemplate: "https://example.invalid/{z}/{x}/{y}.png",
            ZoomLevel: 14,
            GeographicBounds: new GeographicRectangle(35.0, 35.1, 139.0, 139.1),
            MaxTextureSize: 1024);

        ImportedSceneSourceDataset documentSet = new(
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"],
            ["bldg", "dem"],
            [overlay],
            ["53394525"]);
        ImportedSceneSourceSnapshot readResult = new(
            documentSet,
            new ImportedSceneSourceContext([], new GeodeticPoint(35.0, 139.0, 12.5)));

        DefaultImportedSceneMetadataComposer composer = new();

        ImportedSceneMetadata metadata = composer.Compose(
            request,
            readResult);

        Assert.Equal("3.0", metadata.SchemaVersion);
        Assert.Equal("PLATEAU tokyo23ku 53394525", metadata.SceneName);
        Assert.Equal(importRequest, metadata.Request);
        Assert.Equal(documentSet.PackageNames, metadata.SourceDataset.PackageNames);
        Assert.Equal(documentSet.RelativeSourceFiles, metadata.SourceDataset.SourceFiles);
        Assert.Equal(documentSet.SelectedMeshCodes, metadata.SourceDataset.SelectedMeshCodes);
        Assert.Equal("bldg", metadata.SourceDataset.SourceFilePackageNamesByRelativePath?["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);
        Assert.Equal(35.0, metadata.GeodeticOrigin.Latitude);
        Assert.Equal(139.0, metadata.GeodeticOrigin.Longitude);
        Assert.Equal(12.5, metadata.GeodeticOrigin.Altitude);
        Assert.Equal(
            new LicenseMetadata(
                RequireCredit: true,
                CreditText: "Contains PLATEAU dataset content for tokyo23ku. Follow the original PLATEAU dataset terms and provide source attribution when redistributing derived content.",
                LicenseName: "PLATEAU Open Data Terms",
                LicenseUrl: "https://www.mlit.go.jp/plateau/site-policy/"),
            metadata.Attribution.DatasetLicense);
    }

    [Fact]
    public async Task ComposedStreamingSourcePreflightValidatesExplicitDemTextureSource()
    {
        ResolvedLocalPlateauImportRequest request = ResolvedLocalPlateauImportRequestTestFactory.Create(
            packageNames: ["dem"],
            demTextureLocalSourcePath: "C:\\tmp\\plateau\\ortho.tif");
        PlateauImportRequest importRequest = request.ToImportRequest();
        ImportedSceneSourceSnapshot readResult = new(
            new ImportedSceneSourceDataset(
                ["udx/dem/53394525/terrain.gml"],
                ["dem"],
                [
                    new TerrainTextureOverlay(
                        "dem",
                        ThirdRegionalMeshCode.Parse("53394525"),
                        "https://example.invalid/discovered/{z}/{x}/{y}.png",
                        18,
                        new GeographicRectangle(35.0, 35.1, 139.0, 139.1),
                        1024),
                ],
                ["53394525"]),
            new ImportedSceneSourceContext([], new GeodeticPoint(35.0, 139.0, 0.0)));
        RecordingDemTextureSourcePolicy demTextureSourcePolicy = new();
        DefaultImportedSceneSourceComposer composer = new(
            new DefaultImportedSceneMetadataComposer(),
            new ThrowingGeometryProjector(),
            demTextureSourcePolicy);

        IImportedSceneSource source = composer.Compose(
            request,
            readResult,
            new PassthroughImportedObjectUnitOptimizer());

        IImportedSceneSourcePreflight preflight = Assert.IsAssignableFrom<IImportedSceneSourcePreflight>(source);
        await preflight.ValidateBeforeSinkSetupAsync();

        Assert.Equal(1, demTextureSourcePolicy.ResolveCallCount);
        Assert.Equal(importRequest, demTextureSourcePolicy.LastRequest);
        Assert.NotEmpty(demTextureSourcePolicy.LastOverlayRegions!);
    }

    private sealed class ThrowingGeometryProjector : ICityGmlGeometryProjector
    {
        public IEnumerable<ImportedCityObject> ProjectCityObjects(
            CachedSourceFileDescriptor sourceFile,
            CoordinateReferenceSystem referenceSystem,
            GeodeticPoint globalOriginPoint,
            LocalCartesian? globalCartesian,
            IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
            IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
            IReadOnlyList<string> selectedMeshCodes,
            PlateauImportRequest request,
            Func<ParsedCityObject, bool>? predicate = null,
            CancellationToken cancellationToken = default)
        {
            _ = sourceFile;
            _ = referenceSystem;
            _ = globalOriginPoint;
            _ = globalCartesian;
            _ = demTerrainTextureOverlays;
            _ = requestedMeshCodeBounds;
            _ = selectedMeshCodes;
            _ = request;
            _ = predicate;
            _ = cancellationToken;
            throw new InvalidOperationException("Compose should not project geometry.");
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

    private sealed class RecordingDemTextureSourcePolicy : IDemTextureSourcePolicy
    {
        public int ResolveCallCount { get; private set; }

        public PlateauImportRequest? LastRequest { get; private set; }

        public IReadOnlyList<DemTerrainOverlayRegion>? LastOverlayRegions { get; private set; }

        public Task<ResolvedDemTextureSources> ResolveAsync(
            PlateauImportRequest request,
            IReadOnlyList<DemTerrainOverlayRegion> overlayRegions,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveCallCount++;
            LastRequest = request;
            LastOverlayRegions = overlayRegions;
            return Task.FromResult(new ResolvedDemTextureSources(
                [
                    new TerrainTextureOverlay(
                        "dem",
                        ThirdRegionalMeshCode.Parse("53394525"),
                        "https://example.invalid/{z}/{x}/{y}.png",
                        18,
                        overlayRegions[0].GeographicBounds,
                        1024),
                ]));
        }

        public IReadOnlyList<TerrainTextureOverlay> CreateMapTileFallbackOverlays(
            IReadOnlyList<DemTerrainOverlayRegion> overlayRegions)
        {
            _ = overlayRegions;
            return [];
        }
    }
}
