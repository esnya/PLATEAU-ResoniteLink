namespace Plateau.ResoniteLink.Tests.Docs;

public sealed class LiveSendDocumentationContractTests
{
    [Fact]
    public void ReadmePointsToDocsAndSkillForTheirDistinctRoles()
    {
        string readme = File.ReadAllText(TestData.GetRepositoryPath("README.md"));
        string readmeJa = File.ReadAllText(TestData.GetRepositoryPath("README.ja.md"));

        Assert.Contains("docs/live-testing.md", readme, StringComparison.Ordinal);
        Assert.Contains(".agents/skills/resonite-live-send-debug/SKILL.md", readme, StringComparison.Ordinal);
        Assert.Contains("docs/live-testing.ja.md", readmeJa, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentsSeparatesSkillAndOperatorWorkflowRoles()
    {
        string agents = File.ReadAllText(TestData.GetRepositoryPath("AGENTS.md"));
        string agentsJa = File.ReadAllText(TestData.GetRepositoryPath("AGENTS.ja.md"));

        Assert.Contains(".agents/skills/resonite-live-send-debug/SKILL.md", agents, StringComparison.Ordinal);
        Assert.Contains("docs/live-testing.md", agents, StringComparison.Ordinal);
        Assert.Contains("docs/live-testing.ja.md", agentsJa, StringComparison.Ordinal);
    }

    [Fact]
    public void DocsAndSkillAgreeOnTheirDifferentResponsibilities()
    {
        string trackedDoc = File.ReadAllText(TestData.GetRepositoryPath("docs", "live-testing.md"));
        string trackedDocJa = File.ReadAllText(TestData.GetRepositoryPath("docs", "live-testing.ja.md"));
        string skill = File.ReadAllText(TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "SKILL.md"));
        string workflow = File.ReadAllText(TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "references", "workflow.md"));

        Assert.Contains("operator-facing and human-facing live-send workflow reference", trackedDoc, StringComparison.Ordinal);
        Assert.Contains("英語版 live-send 手順書", trackedDocJa, StringComparison.Ordinal);
        Assert.Contains("Coding Agent execution playbook", skill, StringComparison.Ordinal);
        Assert.Contains("docs/live-testing.md", skill, StringComparison.Ordinal);
        Assert.Contains("agent-facing", workflow, StringComparison.Ordinal);
        Assert.Contains("docs/live-testing.md", workflow, StringComparison.Ordinal);
    }
}
