using System.IO;
using System.Linq;

using System.Text.RegularExpressions;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Docs;

public sealed partial class LiveSendDocumentationContractTests
{
    [Fact]
    public void EnglishAndJapaneseMirrorsKeepMatchingMajorHeadings()
    {
        string skill = File.ReadAllText(TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "SKILL.md"));
        string skillJa = File.ReadAllText(TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "SKILL.ja.md"));
        string workflow = File.ReadAllText(TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "references", "workflow.md"));
        string workflowJa = File.ReadAllText(TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "references", "workflow.ja.md"));

        Assert.Equal(GetHeadings(skill), GetHeadings(skillJa));
        Assert.Equal(GetHeadings(workflow), GetHeadings(workflowJa));
    }

    [Fact]
    public void RoadNetworkCoexistenceMirrorsKeepMatchingMajorHeadings()
    {
        string coexistence = File.ReadAllText(TestData.GetRepositoryPath("docs", "road-network-coexistence.md"));
        string coexistenceJa = File.ReadAllText(TestData.GetRepositoryPath("docs", "road-network-coexistence.ja.md"));

        Assert.Equal(GetHeadings(coexistence), GetHeadings(coexistenceJa));
    }

    [Fact]
    public void RoadNetworkCoexistenceDocumentsCurrentRoadPackageBoundary()
    {
        string coexistence = File.ReadAllText(TestData.GetRepositoryPath("docs", "road-network-coexistence.md"));

        foreach (string packageName in PlateauPackageCatalog.RoadPackageNames)
        {
            Assert.Contains($"`{packageName}`", coexistence);
        }

        Assert.Contains("`wwy` is path-like", coexistence);
        Assert.Contains("not a road package", coexistence);
        Assert.Contains("does not suppress generated road-network units by default", coexistence);
    }

    [Fact]
    public void RoadNetworkCoexistenceDocumentsTargetNeutralOptimizerSeam()
    {
        string coexistence = File.ReadAllText(TestData.GetRepositoryPath("docs", "road-network-coexistence.md"));

        Assert.Contains("IImportedObjectUnitOptimizer", coexistence);
        Assert.Contains("CompositeImportedObjectUnitOptimizer", coexistence);
        Assert.Contains("It must not compare by:", coexistence);
        Assert.Contains("Resonite slot names", coexistence);
        Assert.Contains("live-send batching state", coexistence);
        Assert.Contains("does not perform road-network coexistence filtering", coexistence);
    }

    private static string[] GetHeadings(string markdown)
    {
        return HeadingRegex()
            .Matches(markdown)
            .Select(match => match.Value)
            .ToArray();
    }

    [GeneratedRegex("^## .+$", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();
}
