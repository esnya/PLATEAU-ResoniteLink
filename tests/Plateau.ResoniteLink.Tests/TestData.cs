namespace Plateau.ResoniteLink.Tests;

internal static class TestData
{
    private static readonly string RepositoryRoot = GetRepositoryRoot();

    public static string GetFixturePath(string fixtureName)
    {
        return Path.Combine(RepositoryRoot, "tests", "Fixtures", fixtureName);
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Plateau.ResoniteLink.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root could not be located from the test context.");
    }
}
