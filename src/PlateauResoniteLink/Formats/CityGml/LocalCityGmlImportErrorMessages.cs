using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class LocalCityGmlImportErrorMessages
{
    public static string MissingLocalSourcePath()
    {
        return "Local CityGML import requires --citygml-source pointing to either an extracted PLATEAU dataset directory or a .zip/.7z archive.";
    }

    public static string NoMatchingFiles(PlateauImportRequest request, string localSourcePath)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(localSourcePath);

        string fullPath = Path.GetFullPath(localSourcePath);
        string baseMessage =
            $"No local PLATEAU CityGML files were found for mesh code '{request.MeshCode}' in '{fullPath}'. "
            + "Expected files under udx/<package>/<mesh-code>/.";

        string? persistedArchivePath = TryResolvePersistedArchivePath(fullPath);
        if (persistedArchivePath is null)
        {
            return baseMessage;
        }

        return baseMessage
            + " The directory looks like a dataset root created by --work-root. "
            + $"Pass the archive file itself via --citygml-source, for example '{persistedArchivePath}', "
            + "or pass an extracted dataset directory that contains udx/<package>/<mesh-code>/.";
    }

    private static string? TryResolvePersistedArchivePath(string fullPath)
    {
        if (!Directory.Exists(fullPath))
        {
            return null;
        }

        return Directory.EnumerateFiles(fullPath, "source-archive*", SearchOption.TopDirectoryOnly)
            .Where(static path =>
                string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetExtension(path), ".7z", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
