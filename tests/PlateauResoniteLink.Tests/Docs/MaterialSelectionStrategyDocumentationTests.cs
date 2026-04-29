using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PlateauResoniteLink.Tests.Docs;

public sealed partial class MaterialSelectionStrategyDocumentationTests
{
    [Fact]
    public void EnglishAndJapaneseMirrorsKeepMatchingMajorHeadings()
    {
        string english = ReadDoc("material-selection-strategy.md");
        string japanese = ReadDoc("material-selection-strategy.ja.md");

        Assert.Equal(GetHeadings(english), GetHeadings(japanese));
    }

    [Fact]
    public void StrategyDocumentsRequiredIssue154Boundaries()
    {
        string english = ReadDoc("material-selection-strategy.md");

        Assert.Contains("Source `ParameterizedTexture`", english, System.StringComparison.Ordinal);
        Assert.Contains("measured height", english, System.StringComparison.Ordinal);
        Assert.Contains("storey count", english, System.StringComparison.Ordinal);
        Assert.Contains("3.5 m per above-ground storey", english, System.StringComparison.Ordinal);
        Assert.Contains("bbox height", english, System.StringComparison.Ordinal);
        Assert.Contains("`unknown`", english, System.StringComparison.Ordinal);
        Assert.Contains("up to 10 m", english, System.StringComparison.Ordinal);
        Assert.Contains("up to 31 m", english, System.StringComparison.Ordinal);
        Assert.Contains("up to 60 m", english, System.StringComparison.Ordinal);
        Assert.Contains("over 60 m", english, System.StringComparison.Ordinal);
        Assert.Contains("four facade candidates", english, System.StringComparison.Ordinal);
        Assert.Contains("four roof candidates", english, System.StringComparison.Ordinal);
        Assert.Contains("Common material warmup is a setup contract", english, System.StringComparison.Ordinal);
        Assert.Contains("Do not add lazy runtime common-material creation", english, System.StringComparison.Ordinal);
        Assert.Contains("THIRD_PARTY_LICENSES/", english, System.StringComparison.Ordinal);
    }

    [Fact]
    public void StrategyNamesCollectedCandidateInventoryWithoutMakingAllAssetsActive()
    {
        string english = ReadDoc("material-selection-strategy.md");

        string[] expectedCandidateNames =
        [
            "Facade001",
            "Facade005",
            "Facade006",
            "Facade018A",
            "Facade019A",
            "Facade020A",
            "Others0021",
            "Others0022",
            "Others0025",
            "Others0026",
            "Others0029",
        ];

        foreach (string candidateName in expectedCandidateNames)
        {
            Assert.Contains(candidateName, english, System.StringComparison.Ordinal);
        }

        Assert.Contains("must not require creating every collected material", english, System.StringComparison.Ordinal);
    }

    private static string ReadDoc(string fileName)
    {
        return File.ReadAllText(TestData.GetRepositoryPath("docs", fileName));
    }

    private static string[] GetHeadings(string markdown)
    {
        return HeadingRegex()
            .Matches(markdown)
            .Select(static match => match.Value)
            .ToArray();
    }

    [GeneratedRegex("^## .+$", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();
}
