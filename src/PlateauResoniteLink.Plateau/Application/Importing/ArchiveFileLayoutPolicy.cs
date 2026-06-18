using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using PlateauResoniteLink.Core.Application.Importing;
using PlateauResoniteLink.Core.Domain.Importing;

namespace PlateauResoniteLink.Plateau.Application.Importing;

internal sealed class ArchiveFileLayoutPolicy : IArchiveFileLayoutPolicy
{
    public bool IsSupportedArchivePath(string path)
    {
        string extension = Path.GetExtension(path);
        return string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase);
    }

    public string CreateSafePathSegment(string value)
    {
        string normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new PlateauImportValidationException([$"Invalid path segment '{value}'."]);
        }

        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        invalidCharacters = invalidCharacters
            .Concat(new[] { '/', '\\', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar })
            .Distinct()
            .ToArray();

        if (normalized.IndexOfAny(invalidCharacters) >= 0
            || normalized.Equals("..", StringComparison.Ordinal)
            || normalized.Equals(".", StringComparison.Ordinal))
        {
            throw new PlateauImportValidationException([$"Invalid path segment '{value}'."]);
        }

        return normalized;
    }

    public string ResolveDatasetRoot(string workRoot, string dataset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);
        return Path.GetFullPath(Path.Combine(workRoot, CreateSafePathSegment(dataset)));
    }

    public string GetLocalFileCacheRoot(string outputRoot, string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        return Path.Combine(
            Path.GetFullPath(outputRoot),
            "local-file-cache",
            GetLocalFileCacheKey(archivePath));
    }

    public string GetLocalFileCacheKey(string archivePath)
    {
        string fullArchivePath = Path.GetFullPath(archivePath);
        string fileStem = Path.GetFileNameWithoutExtension(fullArchivePath);
        if (string.IsNullOrWhiteSpace(fileStem))
        {
            throw new PlateauImportValidationException(
                [$"The archive path '{archivePath}' must have a non-empty file name before the extension to create a local-file cache key."]);
        }

        string digest = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fullArchivePath)))
            .ToLowerInvariant();
        return $"{fileStem}-{digest[..12]}";
    }

    public string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }

    public string CombineRelativePaths(params string?[] segments)
    {
        return string.Join(
            "/",
            segments
                .Where(static segment => !string.IsNullOrWhiteSpace(segment))
                .Select(segment => NormalizeRelativePath(segment!))
                .Where(static segment => segment.Length > 0));
    }

    public string GetDirectoryPath(string relativePath)
    {
        string normalized = NormalizeRelativePath(relativePath);
        int separatorIndex = normalized.LastIndexOf('/');
        return separatorIndex < 0 ? string.Empty : normalized[..separatorIndex];
    }

    public string? ResolveRelativePath(string baseRelativePath, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return null;
        }

        string normalizedCandidate = candidatePath.Replace('\\', '/').Trim();
        if (normalizedCandidate.StartsWith('/'))
        {
            return null;
        }

        List<string> resolvedSegments = GetDirectoryPath(baseRelativePath)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        foreach (string segment in normalizedCandidate.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (resolvedSegments.Count == 0)
                {
                    return null;
                }

                resolvedSegments.RemoveAt(resolvedSegments.Count - 1);
                continue;
            }

            resolvedSegments.Add(segment);
        }

        string combined = string.Join('/', resolvedSegments);
        return combined.Length == 0 ? null : combined;
    }

    public string ResolveDatasetRootPrefix(IEnumerable<string> relativePaths)
    {
        string[] candidates = relativePaths
            .Select(NormalizeRelativePath)
            .Where(path => path.Contains("/udx/", StringComparison.Ordinal))
            .Select(path => path[..path.IndexOf("/udx/", StringComparison.Ordinal)])
            .Where(static path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path.Count(static character => character == '/'))
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return candidates.FirstOrDefault() ?? string.Empty;
    }

    public string StripDatasetRootPrefix(string relativePath, string datasetRootPrefix)
    {
        string normalizedPath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(datasetRootPrefix))
        {
            return normalizedPath;
        }

        string normalizedPrefix = NormalizeRelativePath(datasetRootPrefix);
        return normalizedPath.StartsWith(normalizedPrefix + "/", StringComparison.OrdinalIgnoreCase)
            ? normalizedPath[(normalizedPrefix.Length + 1)..]
            : normalizedPath;
    }

    public string GetNestedArchivePrefix(string prefix, string entryKey)
    {
        string nestedFileName = Path.GetFileNameWithoutExtension(entryKey);
        string nestedParent = GetDirectoryPath(entryKey);
        return PlateauPackageCatalog.TryNormalizePackageName(nestedFileName, out string? packageName)
            ? CombineRelativePaths(prefix, nestedParent, "udx", packageName)
            : CombineRelativePaths(prefix, nestedParent, nestedFileName);
    }
}
