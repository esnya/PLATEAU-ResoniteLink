using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Tests.Targets;

using CanonicalSceneDumpSink = PlateauResoniteLink.Targets.Resonite.Diagnostics.CanonicalSceneDumpSink;
using DeterministicTerrainTextureAssetGenerator = PlateauResoniteLink.Targets.Resonite.Diagnostics.DeterministicTerrainTextureAssetGenerator;

namespace PlateauResoniteLink.Tests.EndToEnd;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
[Trait("Category", "Slow")]
public sealed class ResoniteSemanticGateTests
{
    private const string UpdateSnapshotsVariable = "PLATEAU_RESONITE_UPDATE_CANONICAL_DUMPS";
    private const string SnapshotName = "material-provider-boundary-53394525.json";

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "CanonicalSceneDumpSink owns the created target for this test.")]
    public async Task ExecuteLocalFixture_ToFakeLink_CanonicalDumpMatchesBaseline()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDatasetMixedObjects");
        PlateauImportRequest request = new(
            "tokyo23ku",
            "53394525",
            DatasetLocation.Local(fixturePath),
            PackageNames: ["bldg", "dem", "tran", "luse"]);
        IImportedSceneSource source = await CreateImportedSceneSourceFactory().CreateAsync(request);
        using TemporaryDirectory workDirectory = new();
        string actualPath = Path.Combine(workDirectory.Path, "canonical-scene.json");
        using SceneSinkRecordingClient client = new();
        await using CanonicalSceneDumpSink dumpSink = new(
            ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(
                client,
                new DeterministicTerrainTextureAssetGenerator()),
            client,
            actualPath);

        _ = await dumpSink.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(source.Metadata, workDirectory.Path),
            source.ReadObjectUnitsAsync());

        string actual = await File.ReadAllTextAsync(actualPath, Encoding.UTF8);
        string snapshotPath = TestData.GetRepositoryPath("tests", "Fixtures", "CanonicalDumps", SnapshotName);
        if (ShouldUpdateSnapshots())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            await File.WriteAllTextAsync(snapshotPath, actual, Encoding.UTF8);
            return;
        }

        string expected = await File.ReadAllTextAsync(snapshotPath, Encoding.UTF8);
        Assert.Equal(expected, actual);
    }

    private static DefaultImportedSceneSourceFactory CreateImportedSceneSourceFactory()
    {
        ArchiveFileLayoutPolicy archiveFileLayoutPolicy = new();
        DefaultPlateauDatasetContentSourceFactory contentSourceFactory = new(
            new RemoteArchiveDistributionPolicy(),
            archiveFileLayoutPolicy);
        DefaultDemTextureSourcePolicy demTextureSourcePolicy = new(
            new DefaultDemTerrainGeoReferencedRasterCatalogFactory(contentSourceFactory));

        return new DefaultImportedSceneSourceFactory(
            new LocalCityGmlDocumentReader(
                contentSourceFactory,
                new CityGmlAppearanceStoreFactory(),
                new CityGmlLodSelector()),
            new DefaultImportedSceneSourceComposer(
                new LocalCityGmlGeometryProjector(new DefaultMaterialResolver(CommonMaterialCatalog.Create())),
                demTextureSourcePolicy),
            demTextureSourcePolicy,
            new CompositeImportedObjectUnitOptimizer(
                [
                    new ImportedDynamicMaterialUvUnitOptimizer(),
                ]));
    }

    private static bool ShouldUpdateSnapshots()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable(UpdateSnapshotsVariable),
            "1",
            StringComparison.Ordinal);
    }
}
