namespace Plateau.ResoniteLink.Tests.Docs;

public sealed class LiveSendDocumentationContractTests
{
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
    public void SkillAndWorkflowKeepDirectHelperCommandSurface()
    {
        string skill = File.ReadAllText(TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "SKILL.md"));
        string skillJa = File.ReadAllText(TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "SKILL.ja.md"));
        string workflow = File.ReadAllText(TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "references", "workflow.md"));
        string workflowJa = File.ReadAllText(TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "references", "workflow.ja.md"));

        Assert.Contains("authoritative live-send workflow reference", skill, StringComparison.Ordinal);
        Assert.Contains("authoritative な live-send workflow reference", skillJa, StringComparison.Ordinal);
        Assert.Contains("run-live-send.ps1", skill, StringComparison.Ordinal);
        Assert.Contains("cleanup-session.ps1", skill, StringComparison.Ordinal);
        Assert.Contains("dump-root-session.ps1", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("docs/live-testing", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("docs/live-testing", skillJa, StringComparison.Ordinal);
        Assert.DoesNotContain("docs/live-testing", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("docs/live-testing", workflowJa, StringComparison.Ordinal);
        Assert.DoesNotContain("check-matsumoto-base-append-heightmap-19001.ps1", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("compare-modes.ps1", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("check-matsumoto-base-append-heightmap-19001.ps1", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("compare-modes.ps1", workflow, StringComparison.Ordinal);
    }
}
