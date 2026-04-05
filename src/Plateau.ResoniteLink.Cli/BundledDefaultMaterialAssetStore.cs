using System.Reflection;

namespace Plateau.ResoniteLink.Cli;

internal static class BundledDefaultMaterialAssetStore
{
    private static readonly object SyncRoot = new();
    private static readonly Assembly Assembly = typeof(BundledDefaultMaterialAssetStore).Assembly;
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

        string resourceName = $"Plateau.ResoniteLink.Cli.Assets.DefaultMaterials.{logicalPath[logicalPrefix.Length..].Replace('/', '.')}";

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
}
