namespace Plateau.ResoniteLink.Tests.Architecture;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class Issue80ArchitectureContractTests
{
    [Fact]
    public void CliAndApplicationServices_DoNotSelfWireConcreteDependencies()
    {
        string cliApplication = File.ReadAllText(TestData.GetRepositoryPath("src", "Plateau.ResoniteLink.Cli", "CliApplication.cs"));
        string inspectionService = File.ReadAllText(TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "UseCases", "Importing", "DatasetInspectionService.cs"));
        string importService = File.ReadAllText(TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "UseCases", "Importing", "PlateauImportService.cs"));

        Assert.DoesNotContain("new DatasetInspectionService(", cliApplication, StringComparison.Ordinal);
        Assert.DoesNotContain("new DefaultPlateauDatasetContentSourceFactory(", inspectionService, StringComparison.Ordinal);
        Assert.DoesNotContain("new ArchiveFileLayoutPolicy(", importService, StringComparison.Ordinal);
    }

    [Fact]
    public void PlateauFormatsAndProfiles_DoNotReferenceResoniteTargetNamespaces()
    {
        string[] files =
        [
            .. Directory.EnumerateFiles(TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "Formats", "CityGml"), "*.cs", SearchOption.AllDirectories),
            .. Directory.EnumerateFiles(TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "Profiles", "PlateauCityGml"), "*.cs", SearchOption.AllDirectories),
        ];

        string[] offenders = files
            .Where(static path =>
            {
                string content = File.ReadAllText(path);
                return content.Contains("using Plateau.ResoniteLink.Targets.Resonite", StringComparison.Ordinal)
                    || content.Contains("Plateau.ResoniteLink.Targets.Resonite.", StringComparison.Ordinal);
            })
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TransportLayer_DoesNotReferenceTargetNamespaces()
    {
        string transportRoot = TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "Transport", "ResoniteLink");
        string[] offenders = Directory
            .EnumerateFiles(transportRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static path =>
            {
                string content = File.ReadAllText(path);
                return content.Contains("using Plateau.ResoniteLink.Targets.Resonite", StringComparison.Ordinal)
                    || content.Contains("Plateau.ResoniteLink.Targets.Resonite.", StringComparison.Ordinal);
            })
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TargetsDoNotDependOnCliResourceNames()
    {
        string bundledAssetStore = File.ReadAllText(
            TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "Targets", "Resonite", "BundledDefaultMaterialAssetStore.cs"));

        Assert.DoesNotContain("Plateau.ResoniteLink.Cli.Assets.DefaultMaterials", bundledAssetStore, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationAndProfileSourceBoundary_DoNotExposeLegacyResoniteConstructionNames()
    {
        string[] files =
        [
            TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "UseCases", "Importing", "SceneImportContractTypes.cs"),
            TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "UseCases", "Importing", "PlateauImportService.cs"),
            TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "Profiles", "PlateauCityGml", "IImportedSceneSource.cs"),
            TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "Profiles", "PlateauCityGml", "IImportedSceneSourceFactory.cs"),
            TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "Profiles", "PlateauCityGml", "IImportedSceneSourceComposer.cs"),
        ];

        string[] offenders = files
            .Where(static path =>
            {
                string content = File.ReadAllText(path);
                return content.Contains("IResoniteConstructionSource", StringComparison.Ordinal)
                    || content.Contains("IResoniteConstructionSourceFactory", StringComparison.Ordinal)
                    || content.Contains("IResoniteConstructionComposer", StringComparison.Ordinal)
                    || content.Contains("ConstructionMetadata(", StringComparison.Ordinal)
                    || content.Contains(" LocalOrigin", StringComparison.Ordinal)
                    || content.Contains("(LocalOrigin", StringComparison.Ordinal);
            })
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void PlateauDatasetContentSourceContract_UsesLocalFileGuaranteeTerminology()
    {
        string sourceContract = File.ReadAllText(
            TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "Sources", "Plateau", "IPlateauDatasetContentSource.cs"));
        string archiveLayoutPolicy = File.ReadAllText(
            TestData.GetRepositoryPath("src", "Plateau.ResoniteLink", "Sources", "Plateau", "ArchiveFileLayoutPolicy.cs"));

        Assert.Contains("EnsureLocalFileAsync", sourceContract, StringComparison.Ordinal);
        Assert.DoesNotContain("MaterializeFileAsync", sourceContract, StringComparison.Ordinal);
        Assert.Contains("local-file-cache", archiveLayoutPolicy, StringComparison.Ordinal);
        Assert.DoesNotContain("\"materialized\"", archiveLayoutPolicy, StringComparison.Ordinal);
    }
}
