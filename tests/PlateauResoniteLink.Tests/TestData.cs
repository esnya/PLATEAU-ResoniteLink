namespace PlateauResoniteLink.Tests;

internal static class TestData
{
    private static readonly string RepositoryRoot = GetRepositoryRoot();

    public static string GetFixturePath(string fixtureName)
    {
        return Path.Combine(RepositoryRoot, "tests", "Fixtures", fixtureName);
    }

    public static string GetRepositoryPath(params string[] relativeSegments)
    {
        return relativeSegments.Aggregate(RepositoryRoot, Path.Combine);
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PlateauResoniteLink.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root could not be located from the test context.");
    }
}
