using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class PersistentTerrainTileCache
{
    private readonly string cacheRoot;

    public PersistentTerrainTileCache(string? cacheRoot)
    {
        this.cacheRoot = Path.GetFullPath(cacheRoot ?? GetDefaultCacheRoot());
    }

    public Task<Stream?> TryOpenTileReadAsync(
        string urlTemplate,
        int zoomLevel,
        int tileX,
        int tileY,
        CancellationToken cancellationToken)
    {
        string cachePath = GetCachePath(urlTemplate, zoomLevel, tileX, tileY);
        if (!File.Exists(cachePath))
        {
            return Task.FromResult<Stream?>(null);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Stream?>(File.OpenRead(cachePath));
        }
        catch (IOException)
        {
            return Task.FromResult<Stream?>(null);
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public async Task WriteTileAsync(
        string urlTemplate,
        int zoomLevel,
        int tileX,
        int tileY,
        Stream encodedContent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(encodedContent);

        string cachePath = GetCachePath(urlTemplate, zoomLevel, tileX, tileY);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        string temporaryPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream temporaryStream = File.Create(temporaryPath))
            {
                await encodedContent.CopyToAsync(temporaryStream, cancellationToken);
            }

            File.Move(temporaryPath, cachePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    private string GetCachePath(string urlTemplate, int zoomLevel, int tileX, int tileY)
    {
        string templateDigest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(urlTemplate)))
            .ToLowerInvariant();
        return Path.Combine(
            cacheRoot,
            templateDigest,
            zoomLevel.ToString(CultureInfo.InvariantCulture),
            tileX.ToString(CultureInfo.InvariantCulture),
            $"{tileY.ToString(CultureInfo.InvariantCulture)}.tile");
    }

    private static string GetDefaultCacheRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlateauResoniteLink",
            "terrain-tile-cache");
    }
}
