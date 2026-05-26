using System;
using System.IO;
using System.Linq;

using System.Text.RegularExpressions;

namespace PlateauResoniteLink.Tests.Docs;

public sealed class LiveSendDocumentationContractTests
{
    private static readonly Regex HeadingRegex = new(
        "^## .+$",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(1));

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

    private static string[] GetHeadings(string markdown)
    {
        return HeadingRegex
            .Matches(markdown)
            .Select(match => match.Value)
            .ToArray();
    }
}
