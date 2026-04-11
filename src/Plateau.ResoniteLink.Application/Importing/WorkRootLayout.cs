namespace Plateau.ResoniteLink.Application.Importing;

internal static class WorkRootLayout
{
    public static string ResolveDatasetRoot(string workRoot, string dataset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        string safeDataset = CreateSafePathSegment(dataset);
        return Path.GetFullPath(Path.Combine(workRoot, safeDataset));
    }

    public static string GetSourceArchivePath(string datasetRoot, string archiveFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveFileName);

        string extension = Path.GetExtension(archiveFileName);
        if (!string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase))
        {
            throw new PlateauImportValidationException(
                [$"The archive file '{archiveFileName}' is not a supported CityGML archive."]);
        }

        return Path.Combine(
            Path.GetFullPath(datasetRoot),
            $"source-archive{extension.ToLowerInvariant()}");
    }

    public static string GetSourceArchiveMetadataPath(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        return $"{archivePath}.meta.json";
    }

    public static string CreateRunRoot(string datasetRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetRoot);
        return Path.Combine(Path.GetFullPath(datasetRoot), "run", Guid.NewGuid().ToString("N"));
    }

    public static string GetMaterializedArchiveRoot(string outputRoot, string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        return Path.Combine(
            Path.GetFullPath(outputRoot),
            "materialized",
            GetMaterializedArchiveCacheKey(archivePath));
    }

    public static string GetMaterializedArchiveCacheKey(string archivePath)
    {
        string fullArchivePath = Path.GetFullPath(archivePath);
        string fileStem = Path.GetFileNameWithoutExtension(fullArchivePath);
        if (string.IsNullOrWhiteSpace(fileStem))
        {
            throw new PlateauImportValidationException(
                [$"The archive path '{archivePath}' must have a non-empty file name before the extension to create a materialized archive cache key."]);
        }

        string digest = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fullArchivePath)))
            .ToLowerInvariant();
        return $"{fileStem}-{digest[..12]}";
    }

    public static string CreateSafePathSegment(string value)
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
}
