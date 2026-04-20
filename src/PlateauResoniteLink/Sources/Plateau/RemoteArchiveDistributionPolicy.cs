namespace PlateauResoniteLink.Application.Importing;

public sealed class RemoteArchiveDistributionPolicy : IRemoteArchiveDistributionPolicy
{
    public bool IsSupportedArchivePath(string path)
    {
        string extension = Path.GetExtension(path);
        return string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase);
    }

    public string GetArchiveFileName(Uri archiveUri)
    {
        string fileName = Path.GetFileName(archiveUri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new PlateauImportValidationException(
                [$"The online resource '{archiveUri}' does not expose a valid archive file name."]);
        }

        return fileName;
    }

    public string GetSourceArchivePath(string datasetRoot, Uri archiveUri, string archiveFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetRoot);
        ArgumentNullException.ThrowIfNull(archiveUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveFileName);

        string extension = Path.GetExtension(archiveFileName);
        if (!IsSupportedArchivePath(archiveFileName))
        {
            throw new PlateauImportValidationException(
                [$"The archive file '{archiveFileName}' is not a supported CityGML archive."]);
        }

        return Path.Combine(
            Path.GetFullPath(datasetRoot),
            $"{GetSourceArchiveFileStem(archiveUri)}{extension.ToLowerInvariant()}");
    }

    public string GetSourceArchiveMetadataPath(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        return $"{archivePath}.meta.json";
    }

    private static string GetSourceArchiveFileStem(Uri archiveUri)
    {
        string archiveIdentity = archiveUri.AbsoluteUri;
        string digest = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(archiveIdentity)))
            .ToLowerInvariant();
        return $"source-archive-{digest[..12]}";
    }
}
