using System.Runtime.CompilerServices;

namespace Plateau.ResoniteLink.Tests;

internal static class TestData
{
    public static string GetFixturePath(string fixtureName)
    {
        return Path.Combine(GetRepositoryRoot(), "tests", "Fixtures", fixtureName);
    }

    private static string GetRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        string? sourceDirectory = Path.GetDirectoryName(sourceFilePath);
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            throw new DirectoryNotFoundException("Source file directory could not be resolved from the test context.");
        }

        DirectoryInfo? current = new(sourceDirectory);
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
