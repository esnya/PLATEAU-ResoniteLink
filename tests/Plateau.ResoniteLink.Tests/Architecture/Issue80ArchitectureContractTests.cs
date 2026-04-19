using System.Diagnostics.CodeAnalysis;

namespace Plateau.ResoniteLink.Tests.Architecture;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class Issue80ArchitectureContractTests
{
    private static readonly string RemovedStaticCompositionName = "PlateauCityGml" + "Composition";
    private static readonly string RemovedImportCompositionName = "PlateauCityGml" + "ImportComposition";
    private static readonly string RemovedStaticTargetFactoryName = "ResoniteSceneImportTarget" + "Factory";

    [Fact]
    public void ResoniteTargets_UpdateComponentAsync_IsOnlyCalledFromDedicatedInterpreter()
    {
        string[] offendingFiles = Directory
            .EnumerateFiles(TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "Targets", "Resonite"), "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.EndsWith("ResoniteComponentUpdateInterpreter.cs", StringComparison.Ordinal))
            .Where(static path =>
            {
                string content = File.ReadAllText(path);
                return content.Contains("UpdateComponentAsync(", StringComparison.Ordinal)
                    || content.Contains("new UpdateComponent", StringComparison.Ordinal)
                    || content.Contains("UpdateComponent)", StringComparison.Ordinal)
                    || content.Contains("UpdateComponent ", StringComparison.Ordinal);
            })
            .ToArray();

        Assert.Empty(offendingFiles);
    }

    [Fact]
    public void ResoniteTargets_RpcExecutionCalls_AppearOnlyInsideExecutionAdapters()
    {
        string executionRoot = TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "Targets", "Resonite");
        string[] allowedFiles =
        [
            "ResoniteSceneBootstrapInterpreter.cs",
            "ResoniteSceneAnchorResolver.cs",
            "ResoniteSceneSlotSnapshot.cs",
            "IResoniteSlotCreator.cs",
            "ResonitePlannedBatchEmissionInterpreter.cs",
            "ResoniteGeometryAssetAssembler.cs",
            "ResoniteMaterialPlanning.cs",
        ];

        string[] offendingFiles = Directory
            .EnumerateFiles(executionRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                string content = File.ReadAllText(path);
                return content.Contains("RunDataModelOperationBatchAsync(", StringComparison.Ordinal)
                    || content.Contains("GetSlotAsync(", StringComparison.Ordinal)
                    || content.Contains("ImportTextureAsync(", StringComparison.Ordinal)
                    || content.Contains("ImportMeshAsync(", StringComparison.Ordinal);
            })
            .Where(path => allowedFiles.All(allowed => !path.EndsWith(allowed, StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(offendingFiles);
    }

    [Fact]
    public void ResoniteTargets_DefaultSessionAndTextureFactories_AppearOnlyInsideAllowedFactories()
    {
        string targetsRoot = TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "Targets", "Resonite");
        string[] allowedFiles =
        [
            "ResoniteLiveSendTargetServiceCollectionExtensions.cs",
        ];

        string[] offendingFiles = Directory
            .EnumerateFiles(targetsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                string content = File.ReadAllText(path);
                return content.Contains("ResoniteLinkTransportSessionFactory.Create(", StringComparison.Ordinal)
                    || content.Contains("new TerrainTextureAssetGenerator(", StringComparison.Ordinal);
            })
            .Where(path => allowedFiles.All(allowed => !path.EndsWith(allowed, StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(offendingFiles);
    }

    [Fact]
    public void SourceTree_DoesNotReferenceRemovedStaticCompositionPaths()
    {
        string[] files =
        [
            .. Directory.EnumerateFiles(TestData.GetRepositoryPath("src"), "*.cs", SearchOption.AllDirectories),
            .. Directory.EnumerateFiles(TestData.GetRepositoryPath("tests"), "*.cs", SearchOption.AllDirectories),
        ];

        string[] offenders = files
            .Where(static path =>
            {
                string content = File.ReadAllText(path);
                return content.Contains(RemovedStaticCompositionName, StringComparison.Ordinal)
                    || content.Contains(RemovedImportCompositionName, StringComparison.Ordinal)
                    || content.Contains(RemovedStaticTargetFactoryName, StringComparison.Ordinal);
            })
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void CityGmlBoundaryLayers_DoNotRetainLegacySnapshotStateOrConstructionSourceBridges()
    {
        string[] candidateFiles =
        [
            TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "Profiles", "PlateauCityGml", "LocalCityGmlBootstrapSnapshots.cs"),
            TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "Profiles", "PlateauCityGml", "LocalCityGmlConstructionSource.cs"),
        ];

        string[] offenders = candidateFiles
            .Where(static path =>
            {
                string content = File.ReadAllText(path);
                return content.Contains(" Legacy " + "{ get; }", StringComparison.Ordinal)
                    || content.Contains(".ToLegacy()", StringComparison.Ordinal);
            })
            .ToArray();

        Assert.Empty(offenders);
    }
}
