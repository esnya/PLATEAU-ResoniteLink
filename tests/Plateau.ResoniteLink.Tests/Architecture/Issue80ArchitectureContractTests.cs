namespace Plateau.ResoniteLink.Tests.Architecture;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class Issue80ArchitectureContractTests
{
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
            "PlannedBatchEmissionInterpreter.cs",
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
                    || content.Contains("ImportMeshAsync(", StringComparison.Ordinal)
                    || content.Contains("AddSlotAsync(", StringComparison.Ordinal)
                    || content.Contains("AddComponentAsync(", StringComparison.Ordinal);
            })
            .Where(path => allowedFiles.All(allowed => !path.EndsWith(allowed, StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(offendingFiles);
    }

    [Fact]
    public void SourceTree_NoLongerReferencesRemovedStaticCompositionOrFactoryNames()
    {
        string[] files =
        [
            .. Directory.EnumerateFiles(TestData.GetRepositoryPath("src"), "*.cs", SearchOption.AllDirectories),
            .. Directory.EnumerateFiles(TestData.GetRepositoryPath("tests"), "*.cs", SearchOption.AllDirectories),
        ];

        string[] offenders = files
            .Where(static path => !path.EndsWith("Issue80ArchitectureContractTests.cs", StringComparison.Ordinal))
            .Where(static path =>
            {
                string content = File.ReadAllText(path);
                return content.Contains("PlateauCityGmlComposition", StringComparison.Ordinal)
                    || content.Contains("PlateauCityGmlImportComposition", StringComparison.Ordinal)
                    || content.Contains("PlateauCityGmlConstructionSources", StringComparison.Ordinal)
                    || content.Contains("ResoniteSceneImportTargetFactory", StringComparison.Ordinal)
                    || content.Contains("ReadDocumentSetAsync(", StringComparison.Ordinal);
            })
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void CityGmlBoundaryLayers_DoNotCallLegacyBridgeHelpers()
    {
        string[] files =
        [
            .. Directory.EnumerateFiles(TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "Formats", "CityGml"), "*.cs", SearchOption.AllDirectories),
            .. Directory.EnumerateFiles(TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "Profiles", "PlateauCityGml"), "*.cs", SearchOption.AllDirectories),
            TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "Profiles", "PlateauCityGml", "LocalCityGmlBootstrapPipeline.cs"),
            TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "Profiles", "PlateauCityGml", "LocalCityGmlConstructionSource.cs"),
        ];

        string[] offenders = files
            .Where(static path =>
            {
                string content = File.ReadAllText(path);
                return content.Contains("LocalCityGmlLegacyProjectionBridge", StringComparison.Ordinal)
                    || content.Contains("ICityGmlLegacyProjectionBridge", StringComparison.Ordinal);
            })
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TransportWrappers_NoLongerReferenceLegacyCompatibilityShim()
    {
        string transportRoot = TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "Transport", "ResoniteLink");
        string[] offenders = Directory
            .EnumerateFiles(transportRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static path =>
            {
                string content = File.ReadAllText(path);
                return content.Contains("ResoniteLinkLegacyCompatibility", StringComparison.Ordinal);
            })
            .ToArray();

        Assert.Empty(offenders);
    }
}
