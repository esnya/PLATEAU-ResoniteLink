using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace PlateauResoniteLink.Targets.Resonite;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1822:Mark members as static",
    Justification = "The asset store intentionally stays instance-based so bundled material resolution can remain a replaceable target-local seam.")]
internal sealed class BundledDefaultMaterialAssetStore
{
    private const string ResourceRoot = "PlateauResoniteLink.Assets.DefaultMaterials.";
    private static readonly object SyncRoot = new();
    private static readonly Assembly Assembly = typeof(BundledDefaultMaterialAssetStore).Assembly;
    private static readonly HashSet<string> ResourceNames = Assembly
        .GetManifestResourceNames()
        .ToHashSet(StringComparer.Ordinal);
    private static readonly AsyncLocal<string?> ExtractionRootOverride = new();
    private static readonly string DefaultExtractionRoot = Path.Combine(
        Path.GetTempPath(),
        "PlateauResoniteLink",
        "default-materials",
        Assembly.ManifestModule.ModuleVersionId.ToString("N"));

    public string GetAbsolutePath(string logicalPath)
    {
        const string logicalPrefix = "default-materials/";
        if (!logicalPath.StartsWith(logicalPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Bundled material path must start with '{logicalPrefix}', but was '{logicalPath}'.");
        }

        string resourceName = GetResourceName(logicalPath);

        string absolutePath = Path.Combine(
            GetExtractionRoot(),
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

    public bool TryGetAbsolutePath(string logicalPath, out string absolutePath)
    {
        if (!TryGetResourceName(logicalPath, out _))
        {
            absolutePath = string.Empty;
            return false;
        }

        absolutePath = GetAbsolutePath(logicalPath);
        return true;
    }

    internal IDisposable PushExtractionRootOverride(string extractionRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extractionRoot);

        string? previousRoot = ExtractionRootOverride.Value;
        ExtractionRootOverride.Value = extractionRoot;
        return new ExtractionRootOverrideScope(previousRoot);
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

        string relativePath = logicalPath[logicalPrefix.Length..];
        string resourceSuffix = relativePath.Replace('/', '.');
        resourceName = $"{ResourceRoot}{resourceSuffix}";
        if (ResourceNames.Contains(resourceName))
        {
            return true;
        }

        string normalizedResourceSuffix = CreateNormalizedResourceSuffix(relativePath);
        string normalizedResourceName = $"{ResourceRoot}{normalizedResourceSuffix}";
        if (ResourceNames.Contains(normalizedResourceName))
        {
            resourceName = normalizedResourceName;
            return true;
        }

        foreach (string candidateResourceName in ResourceNames)
        {
            if (candidateResourceName.EndsWith(normalizedResourceSuffix, StringComparison.Ordinal))
            {
                resourceName = candidateResourceName;
                return true;
            }
        }

        return false;
    }

    private static string CreateNormalizedResourceSuffix(string relativePath)
    {
        string[] segments = relativePath.Split('/');
        if (segments.Length == 0)
        {
            return string.Empty;
        }

        for (int index = 0; index < segments.Length - 1; index++)
        {
            segments[index] = segments[index].Replace('-', '_');
        }

        return string.Join('.', segments);
    }

    private static string GetExtractionRoot()
    {
        return ExtractionRootOverride.Value ?? DefaultExtractionRoot;
    }

    private sealed class ExtractionRootOverrideScope(string? previousRoot) : IDisposable
    {
        private readonly string? previousRoot = previousRoot;
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            ExtractionRootOverride.Value = previousRoot;
            disposed = true;
        }
    }
}
