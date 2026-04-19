using System.Text.RegularExpressions;

namespace Plateau.ResoniteLink.Tests.Architecture;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed partial class Issue80ArchitectureContractTests
{
    private const string RemovedLiveTargetName = "ResoniteLink" + "SceneBuilder";
    private const string RemovedProjectionName = "LocalCityGml" + "ResonitePlanBuilder";

    private static readonly string[] AllowedSuffixes =
    [
        "Interpreter",
        "Policy",
        "Plan",
        "State",
        "Snapshot",
        "Result",
        "Target",
        "Factory",
    ];

    [Fact]
    public void RepoAssembly_DoesNotExposeBannedBehaviorSinkTypeNames()
    {
        Type[] bannedTypes = typeof(Plateau.ResoniteLink.Application.Importing.PlateauCityGmlConstructionSources).Assembly
            .GetTypes()
            .Where(static type => type.Namespace is not null && type.Namespace.StartsWith("Plateau.ResoniteLink", StringComparison.Ordinal))
            .Where(static type => BannedTypeNameRegex().IsMatch(type.Name))
            .Where(type => AllowedSuffixes.All(allowed => !type.Name.EndsWith(allowed, StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(bannedTypes);
    }

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
    public void SourceTree_NoLongerReferencesRemovedIssue80Names()
    {
        string[] files =
        [
            .. Directory.EnumerateFiles(TestData.GetRepositoryPath("src"), "*.cs", SearchOption.AllDirectories),
            .. Directory.EnumerateFiles(TestData.GetRepositoryPath("tests"), "*.cs", SearchOption.AllDirectories),
            TestData.GetRepositoryPath("README.md"),
            TestData.GetRepositoryPath("README.ja.md"),
            TestData.GetRepositoryPath("AGENTS.md"),
            TestData.GetRepositoryPath("AGENTS.ja.md"),
        ];

        string[] offenders = files
            .Where(static path =>
            {
                string content = File.ReadAllText(path);
                return content.Contains(RemovedLiveTargetName, StringComparison.Ordinal)
                    || content.Contains(RemovedProjectionName, StringComparison.Ordinal);
            })
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SourceTree_FilePaths_NoLongerUseRemovedIssue80Names()
    {
        string[] candidates =
        [
            .. Directory.EnumerateFiles(TestData.GetRepositoryPath("src"), "*", SearchOption.AllDirectories),
            .. Directory.EnumerateFiles(TestData.GetRepositoryPath("tests"), "*", SearchOption.AllDirectories),
        ];
        string[] offenders = candidates
            .Where(static path =>
                path.Contains(RemovedLiveTargetName, StringComparison.Ordinal)
                || path.Contains(RemovedProjectionName, StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(offenders);
    }

    [GeneratedRegex("(Builder|Manager|Coordinator|Helper|Util)$", RegexOptions.CultureInvariant)]
    private static partial Regex BannedTypeNameRegex();
}
