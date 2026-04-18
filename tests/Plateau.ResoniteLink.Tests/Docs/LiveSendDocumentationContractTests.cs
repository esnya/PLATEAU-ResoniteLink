using System.Text.RegularExpressions;

namespace Plateau.ResoniteLink.Tests.Docs;

public sealed partial class LiveSendDocumentationContractTests
{
    private static readonly string[] PublicHelperScripts =
    [
        "discover-session.ps1",
        "start-headless-session.ps1",
        "stop-headless-session.ps1",
        "cleanup-session.ps1",
        "dump-root-session.ps1",
        "run-live-send.ps1",
    ];

    private static readonly string[] FixtureOnlyStrings =
    [
        "plateau-20202-matsumoto-shi-2020",
        "54372778",
        "54372788",
        "53391530",
        "19001",
        "workspace-matsumoto-local-bounds-eval-20260418-180506.json",
    ];

    [Fact]
    public void ReadmePointsOnlyToSkillForLiveSendWorkflow()
    {
        string readme = File.ReadAllText(TestData.GetRepositoryPath("README.md"));
        string readmeJa = File.ReadAllText(TestData.GetRepositoryPath("README.ja.md"));

        Assert.Contains(".agents/skills/resonite-live-send-debug/SKILL.md", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("docs/live-testing", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("docs/live-testing", readmeJa, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentsPointsOnlyToSkillForLiveSendWorkflow()
    {
        string agents = File.ReadAllText(TestData.GetRepositoryPath("AGENTS.md"));
        string agentsJa = File.ReadAllText(TestData.GetRepositoryPath("AGENTS.ja.md"));

        Assert.Contains(".agents/skills/resonite-live-send-debug/SKILL.md", agents, StringComparison.Ordinal);
        Assert.DoesNotContain("docs/live-testing", agents, StringComparison.Ordinal);
        Assert.DoesNotContain("docs/live-testing", agentsJa, StringComparison.Ordinal);
    }

    [Fact]
    public void SkillStaysAsEntryPointWhileWorkflowOwnsFixtures()
    {
        string skill = File.ReadAllText(TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "SKILL.md"));
        string skillJa = File.ReadAllText(TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "SKILL.ja.md"));

        Assert.Contains("references/workflow.md", skill, StringComparison.Ordinal);
        Assert.Contains("references/workflow.md", skillJa, StringComparison.Ordinal);

        foreach (string helper in PublicHelperScripts)
        {
            Assert.Contains(helper, skill, StringComparison.Ordinal);
            Assert.Contains(helper, skillJa, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("windows-build-tools.ps1", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("windows-build-tools.ps1", skillJa, StringComparison.Ordinal);

        foreach (string fixtureOnlyString in FixtureOnlyStrings)
        {
            Assert.DoesNotContain(fixtureOnlyString, skill, StringComparison.Ordinal);
            Assert.DoesNotContain(fixtureOnlyString, skillJa, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WorkflowOwnsFixtureEnvironmentAndReferenceGuidance()
    {
        string workflow = File.ReadAllText(TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "references", "workflow.md"));
        string workflowJa = File.ReadAllText(TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "references", "workflow.ja.md"));

        Assert.Contains("## Defaults", workflow, StringComparison.Ordinal);
        Assert.Contains("## Environment Selection", workflow, StringComparison.Ordinal);
        Assert.Contains("## Fixed Run Worksheet", workflow, StringComparison.Ordinal);
        Assert.Contains("## Matsumoto Reference Values", workflow, StringComparison.Ordinal);
        Assert.Contains("## Public Helper Commands", workflow, StringComparison.Ordinal);

        Assert.Contains("## Defaults", workflowJa, StringComparison.Ordinal);
        Assert.Contains("## Environment Selection", workflowJa, StringComparison.Ordinal);
        Assert.Contains("## Fixed Run Worksheet", workflowJa, StringComparison.Ordinal);
        Assert.Contains("## Matsumoto Reference Values", workflowJa, StringComparison.Ordinal);
        Assert.Contains("## Public Helper Commands", workflowJa, StringComparison.Ordinal);

        foreach (string fixtureOnlyString in FixtureOnlyStrings)
        {
            Assert.Contains(fixtureOnlyString, workflow, StringComparison.Ordinal);
            Assert.Contains(fixtureOnlyString, workflowJa, StringComparison.Ordinal);
        }

        foreach (string helper in PublicHelperScripts)
        {
            Assert.Contains(helper, workflow, StringComparison.Ordinal);
            Assert.Contains(helper, workflowJa, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("- `scripts/windows-build-tools.ps1`", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("- `scripts/windows-build-tools.ps1`", workflowJa, StringComparison.Ordinal);
        Assert.Contains("internal shared helper", workflow, StringComparison.Ordinal);
        Assert.Contains("internal shared helper", workflowJa, StringComparison.Ordinal);
        Assert.DoesNotContain("ResoniteAdmin", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("ResoniteAdmin", workflowJa, StringComparison.Ordinal);
        Assert.Contains("ResoniteSessionTool.csproj", workflow, StringComparison.Ordinal);
        Assert.Contains("ResoniteSessionTool.csproj", workflowJa, StringComparison.Ordinal);
        Assert.Contains("`jq` is optional convenience", workflow, StringComparison.Ordinal);
        Assert.Contains("`jq` は post-dump inspection", workflowJa, StringComparison.Ordinal);
    }

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
    public void FixtureStringsDoNotLeakIntoHelperScripts()
    {
        string scriptsRoot = TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "scripts");
        string combinedScripts = string.Join(
            Environment.NewLine,
            Directory.GetFiles(scriptsRoot, "*.ps1", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));

        foreach (string fixtureOnlyString in FixtureOnlyStrings)
        {
            Assert.DoesNotContain(fixtureOnlyString, combinedScripts, StringComparison.Ordinal);
        }
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
