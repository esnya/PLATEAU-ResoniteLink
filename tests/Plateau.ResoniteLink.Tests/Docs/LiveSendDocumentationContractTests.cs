using System.Text.RegularExpressions;

namespace Plateau.ResoniteLink.Tests.Docs;

public sealed partial class LiveSendDocumentationContractTests
{
    private static readonly string[] DeprecatedHelperScripts =
    [
        "discover-session.ps1",
        "start-headless-session.ps1",
        "stop-headless-session.ps1",
        "cleanup-session.ps1",
        "dump-root-session.ps1",
        "run-live-send.ps1",
        "run-live-send-monitored.ps1",
        "windows-build-tools.ps1",
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
    public void SkillDefinesDirectCliAndToolSurfaceOnly()
    {
        string skill = File.ReadAllText(TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "SKILL.md"));
        string skillJa = File.ReadAllText(TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "SKILL.ja.md"));
        string promptConfig = File.ReadAllText(TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "agents", "openai.yaml"));

        Assert.Contains("references/workflow.md", skill, StringComparison.Ordinal);
        Assert.Contains("references/workflow.md", skillJa, StringComparison.Ordinal);
        Assert.Contains("dotnet run --project src/Plateau.ResoniteLink.Cli/Plateau.ResoniteLink.Cli.csproj -- build", skill, StringComparison.Ordinal);
        Assert.Contains("dotnet run --project src/Plateau.ResoniteLink.Cli/Plateau.ResoniteLink.Cli.csproj -- build", skillJa, StringComparison.Ordinal);
        Assert.Contains("dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- discover-session", skill, StringComparison.Ordinal);
        Assert.Contains("dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- discover-session", skillJa, StringComparison.Ordinal);
        Assert.Contains("dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot", skill, StringComparison.Ordinal);
        Assert.Contains("dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot", skillJa, StringComparison.Ordinal);
        Assert.Contains("dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- remove-slot", skill, StringComparison.Ordinal);
        Assert.Contains("dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- remove-slot", skillJa, StringComparison.Ordinal);
        Assert.Contains("dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- start-headless", skill, StringComparison.Ordinal);
        Assert.Contains("dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- start-headless", skillJa, StringComparison.Ordinal);
        Assert.Contains("dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- stop-headless", skill, StringComparison.Ordinal);
        Assert.Contains("dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- stop-headless", skillJa, StringComparison.Ordinal);
        Assert.DoesNotContain("cleanup-dataset-root", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("cleanup-dataset-root", skillJa, StringComparison.Ordinal);
        Assert.DoesNotContain("ResoniteSessionTool.csproj", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("ResoniteSessionTool.csproj", skillJa, StringComparison.Ordinal);
        Assert.Contains("file-based session-tool.cs commands", promptConfig, StringComparison.Ordinal);
        Assert.DoesNotContain("ResoniteSessionTool", promptConfig, StringComparison.Ordinal);

        foreach (string deprecated in DeprecatedHelperScripts)
        {
            Assert.DoesNotContain(deprecated, skill, StringComparison.Ordinal);
            Assert.DoesNotContain(deprecated, skillJa, StringComparison.Ordinal);
        }

        foreach (string fixtureOnlyString in FixtureOnlyStrings)
        {
            Assert.DoesNotContain(fixtureOnlyString, skill, StringComparison.Ordinal);
            Assert.DoesNotContain(fixtureOnlyString, skillJa, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WorkflowOwnsFixtureReferenceAndDirectCommandGuidance()
    {
        string workflow = File.ReadAllText(TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "references", "workflow.md"));
        string workflowJa = File.ReadAllText(TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "references", "workflow.ja.md"));

        Assert.Contains("## Defaults", workflow, StringComparison.Ordinal);
        Assert.Contains("## Agent Guardrails", workflow, StringComparison.Ordinal);
        Assert.Contains("## Fixed Run Worksheet", workflow, StringComparison.Ordinal);
        Assert.Contains("## Direct Command Surface", workflow, StringComparison.Ordinal);
        Assert.Contains("## Matsumoto Reference Values", workflow, StringComparison.Ordinal);

        Assert.Contains("## Defaults", workflowJa, StringComparison.Ordinal);
        Assert.Contains("## Agent Guardrails", workflowJa, StringComparison.Ordinal);
        Assert.Contains("## Fixed Run Worksheet", workflowJa, StringComparison.Ordinal);
        Assert.Contains("## Direct Command Surface", workflowJa, StringComparison.Ordinal);
        Assert.Contains("## Matsumoto Reference Values", workflowJa, StringComparison.Ordinal);

        Assert.DoesNotContain("## Environment Selection", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("## Environment Selection", workflowJa, StringComparison.Ordinal);
        Assert.DoesNotContain("Run helpers from Windows when", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("WSL-driven sender is valid", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("runtime/windows/headless", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("runtime/windows/headless", workflowJa, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet.exe", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet.exe", workflowJa, StringComparison.Ordinal);
        Assert.DoesNotContain("cleanup-dataset-root", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("cleanup-dataset-root", workflowJa, StringComparison.Ordinal);
        Assert.DoesNotContain("ResoniteSessionTool.csproj", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("ResoniteSessionTool.csproj", workflowJa, StringComparison.Ordinal);
        Assert.Contains("tools/session-tool.cs", workflow, StringComparison.Ordinal);
        Assert.Contains("tools/session-tool.cs", workflowJa, StringComparison.Ordinal);
        Assert.Contains("same environment", workflow, StringComparison.Ordinal);
        Assert.Contains("同一 environment", workflowJa, StringComparison.Ordinal);
        Assert.Contains("If `--headless-path` is omitted on Windows", workflow, StringComparison.Ordinal);
        Assert.Contains("`--headless-path` を省略した場合", workflowJa, StringComparison.Ordinal);
        Assert.Contains("--resonitelink-port 19001", workflow, StringComparison.Ordinal);
        Assert.Contains("--resonitelink-port 19001", workflowJa, StringComparison.Ordinal);

        foreach (string fixtureOnlyString in FixtureOnlyStrings)
        {
            Assert.Contains(fixtureOnlyString, workflow, StringComparison.Ordinal);
            Assert.Contains(fixtureOnlyString, workflowJa, StringComparison.Ordinal);
        }

        Assert.Contains("Plateau.ResoniteLink.Cli.csproj", workflow, StringComparison.Ordinal);
        Assert.Contains("Plateau.ResoniteLink.Cli.csproj", workflowJa, StringComparison.Ordinal);
        Assert.Contains("`jq` is optional convenience", workflow, StringComparison.Ordinal);
        Assert.Contains("`jq` は post-dump inspection", workflowJa, StringComparison.Ordinal);

        foreach (string deprecated in DeprecatedHelperScripts)
        {
            Assert.DoesNotContain(deprecated, workflow, StringComparison.Ordinal);
            Assert.DoesNotContain(deprecated, workflowJa, StringComparison.Ordinal);
        }
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
    public void DeprecatedHelperScriptsAreRemovedFromSkillScriptsDirectory()
    {
        string scriptsRoot = TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "scripts");
        if (!Directory.Exists(scriptsRoot))
        {
            return;
        }

        Assert.Empty(Directory.GetFiles(scriptsRoot, "*.ps1", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void SkillToolSurfaceUsesSingleFileScript()
    {
        string toolsRoot = TestData.GetRepositoryPath(".agents", "skills", "resonite-live-send-debug", "tools");
        string scriptPath = Path.Combine(toolsRoot, "session-tool.cs");

        Assert.True(File.Exists(scriptPath));
        Assert.False(File.Exists(Path.Combine(toolsRoot, "ResoniteSessionTool", "ResoniteSessionTool.csproj")));
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
