namespace Plateau.ResoniteLink.Application.Importing;

internal static class PlateauDatasetPathResolver
{
    public static string ResolveDatasetRoot(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        string fullPath = Path.GetFullPath(sourcePath);
        if (PlateauDatasetContentSourceFactory.IsReparsePoint(fullPath))
        {
            throw new ArgumentException(
                $"The path '{sourcePath}' cannot use a symbolic link or junction as the dataset root.",
                nameof(sourcePath));
        }

        if (Directory.Exists(Path.Combine(fullPath, "udx")))
        {
            return fullPath;
        }

        string? candidate = EnumerateDirectoriesSkippingReparsePoints(fullPath)
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

    private static IEnumerable<string> EnumerateDirectoriesSkippingReparsePoints(string rootPath)
    {
        Stack<string> pending = new();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string directory in Directory.EnumerateDirectories(current))
            {
                if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                yield return directory;
                pending.Push(directory);
            }
        }
    }
}
