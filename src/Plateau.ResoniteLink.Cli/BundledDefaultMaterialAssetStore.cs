using System.Reflection;

namespace Plateau.ResoniteLink.Cli;

internal static class BundledDefaultMaterialAssetStore
{
    private static readonly object SyncRoot = new();
    private static readonly Assembly Assembly = typeof(BundledDefaultMaterialAssetStore).Assembly;
    private static readonly HashSet<string> ResourceNames = Assembly
        .GetManifestResourceNames()
        .ToHashSet(StringComparer.Ordinal);
    private static readonly string ExtractionRoot = Path.Combine(
        Path.GetTempPath(),
        "Plateau.ResoniteLink",
        "default-materials");

    public static string GetAbsolutePath(string logicalPath)
    {
        const string logicalPrefix = "default-materials/";
        if (!logicalPath.StartsWith(logicalPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Bundled material path must start with '{logicalPrefix}', but was '{logicalPath}'.");
        }

        string resourceName = GetResourceName(logicalPath);

        string absolutePath = Path.Combine(
            ExtractionRoot,
            logicalPath.Replace('/', Path.DirectorySeparatorChar));
        string? directory = Path.GetDirectoryName(absolutePath);
        if (directory is null)
        {
            throw new InvalidOperationException($"Could not determine extraction directory for '{logicalPath}'.");
        }

        lock (SyncRoot)
        {
            if (File.Exists(absolutePath))
            {
                return absolutePath;
            }

            Directory.CreateDirectory(directory);
            using Stream resourceStream = Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
            using FileStream fileStream = new(
                absolutePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read);
            resourceStream.CopyTo(fileStream);
        }

        return absolutePath;
    }

    public static bool TryGetAbsolutePath(string logicalPath, out string absolutePath)
    {
        if (!TryGetResourceName(logicalPath, out _))
        {
            absolutePath = string.Empty;
            return false;
        }

        absolutePath = GetAbsolutePath(logicalPath);
        return true;
    }

    private static string GetResourceName(string logicalPath)
    {
        return TryGetResourceName(logicalPath, out string? resourceName)
            ? resourceName
            : throw new InvalidOperationException($"Embedded resource for '{logicalPath}' was not found.");
    }

    private static bool TryGetResourceName(string logicalPath, out string resourceName)
    {
        const string logicalPrefix = "default-materials/";
        if (!logicalPath.StartsWith(logicalPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Bundled material path must start with '{logicalPrefix}', but was '{logicalPath}'.");
        }

        resourceName = $"Plateau.ResoniteLink.Cli.Assets.DefaultMaterials.{logicalPath[logicalPrefix.Length..].Replace('/', '.')}";
        if (ResourceNames.Contains(resourceName))
        {
            return true;
        }

        string normalizedResourceName = resourceName.Replace('-', '_');
        if (ResourceNames.Contains(normalizedResourceName))
        {
            resourceName = normalizedResourceName;
            return true;
        }

        return false;
    }
}
