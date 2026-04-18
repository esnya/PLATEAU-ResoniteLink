namespace Plateau.ResoniteLink.Application.Importing;

internal static class PlateauDatasetPathResolver
{
    public static string ResolveDatasetRoot(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        string fullPath = Path.GetFullPath(sourcePath);
        if (Directory.Exists(Path.Combine(fullPath, "udx")))
        {
            return fullPath;
        }

        string? candidate = Directory
            .EnumerateDirectories(fullPath, "*", SearchOption.AllDirectories)
            .Where(path => Directory.Exists(Path.Combine(path, "udx")))
            .OrderBy(path => NormalizePath(Path.GetRelativePath(fullPath, path)).Count(static character => character == '/'))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return candidate ?? fullPath;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}
