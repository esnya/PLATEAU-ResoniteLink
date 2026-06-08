using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1822:Mark members as static",
    Justification = "The asset store intentionally stays instance-based so bundled material resolution can remain a replaceable target-local seam.")]
internal sealed class BundledDefaultMaterialAssetStore
{
    private const string ResourceRoot = "PlateauResoniteLink.Assets.DefaultMaterials.";
    private static readonly Assembly Assembly = typeof(BundledDefaultMaterialAssetStore).Assembly;
    private static readonly HashSet<string> ResourceNames = Assembly
        .GetManifestResourceNames()
        .ToHashSet(StringComparer.Ordinal);

    public Stream OpenRead(string logicalPath)
    {
        string resourceName = GetResourceName(logicalPath);
        return Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
    }

    public Stream OpenRead(BundledDefaultTextureAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return OpenRead(asset.LogicalPath);
    }

    public byte[] ReadAllBytes(BundledDefaultTextureAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        using Stream stream = OpenRead(asset);
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public bool Contains(string logicalPath)
    {
        return TryGetResourceName(logicalPath, out _);
    }

    public bool Contains(BundledDefaultTextureAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Contains(asset.LogicalPath);
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
}
