using System;
using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public static class PlateauSourceFilePackageIndex
{
    public static IReadOnlyDictionary<string, string> CreateByRelativePath(IEnumerable<string> relativeSourceFiles)
    {
        ArgumentNullException.ThrowIfNull(relativeSourceFiles);

        Dictionary<string, string> packageNamesByRelativePath = new(StringComparer.Ordinal);
        foreach (string relativeSourceFile in relativeSourceFiles)
        {
            if (TryResolvePackageName(relativeSourceFile, out string packageName))
            {
                packageNamesByRelativePath[relativeSourceFile] = packageName;
            }
        }

        return packageNamesByRelativePath;
    }

    private static bool TryResolvePackageName(string? relativeSourceFile, out string packageName)
    {
        packageName = string.Empty;
        if (string.IsNullOrWhiteSpace(relativeSourceFile))
        {
            return false;
        }

        string normalizedPath = relativeSourceFile.Replace('\\', '/');
        string[] segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < segments.Length - 1; index++)
        {
            if (string.Equals(segments[index], "udx", StringComparison.OrdinalIgnoreCase))
            {
                return PlateauPackageCatalog.TryNormalizePackageName(segments[index + 1], out packageName);
            }
        }

        return segments.Length >= 2
            && PlateauPackageCatalog.TryNormalizePackageName(segments[0], out packageName);
    }
}
