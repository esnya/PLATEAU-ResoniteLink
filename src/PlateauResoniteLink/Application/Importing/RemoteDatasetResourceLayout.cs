using System;
using System.IO;

namespace PlateauResoniteLink.Application.Importing;

internal static class RemoteDatasetResourceLayout
{
    public static string GetRemoteResourcePath(string datasetRoot, Uri resourceUri, string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetRoot);
        ArgumentNullException.ThrowIfNull(resourceUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        string extension = Path.GetExtension(resourceUri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new PlateauImportValidationException(
                [$"The online resource '{resourceUri}' does not expose a usable file extension."]);
        }

        string digest = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(resourceUri.AbsoluteUri)))
            .ToLowerInvariant();
        return Path.Combine(
            Path.GetFullPath(datasetRoot),
            $"{prefix}-{digest[..12]}{extension.ToLowerInvariant()}");
    }

    public static bool MatchesRemoteResourcePath(string datasetRoot, Uri resourceUri, string prefix, string? localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(localPath),
            GetRemoteResourcePath(datasetRoot, resourceUri, prefix),
            StringComparison.OrdinalIgnoreCase);
    }

    public static string GetRemoteResourceMetadataPath(string resourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);
        return $"{resourcePath}.meta.json";
    }
}
