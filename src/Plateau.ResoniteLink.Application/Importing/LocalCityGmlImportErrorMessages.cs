using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal static class LocalCityGmlImportErrorMessages
{
    public static string MissingLocalSourcePath()
    {
        return "Local CityGML import requires --source local and --local-source-path pointing to either an extracted PLATEAU dataset directory or a .zip/.7z archive.";
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
            + $"Pass the archive file itself via --local-source-path, for example '{persistedArchivePath}', "
            + "or pass an extracted dataset directory that contains udx/<package>/<mesh-code>/.";
    }

    private static string? TryResolvePersistedArchivePath(string fullPath)
    {
        if (!Directory.Exists(fullPath))
        {
            return null;
        }

        string zipPath = Path.Combine(fullPath, "source-archive.zip");
        if (File.Exists(zipPath))
        {
            return zipPath;
        }

        string sevenZipPath = Path.Combine(fullPath, "source-archive.7z");
        return File.Exists(sevenZipPath) ? sevenZipPath : null;
    }
}
