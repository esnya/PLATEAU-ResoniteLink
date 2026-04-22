using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class HttpFileCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<CachedHttpContent> GetOrFetchAsync(
        HttpClient httpClient,
        Uri resourceUri,
        string contentPath,
        string metadataPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(resourceUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataPath);

        Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);

        bool hadCachedContent = File.Exists(contentPath);
        HttpCacheMetadata? metadata = await TryReadMetadataAsync(metadataPath, cancellationToken);
        if (hadCachedContent)
        {
            CachedHttpContent? reused = await TryReuseCachedContentAsync(
                httpClient,
                resourceUri,
                contentPath,
                metadataPath,
                metadata,
                cancellationToken);
            if (reused is not null)
            {
                return reused;
            }
        }

        return await DownloadContentAsync(
            httpClient,
            resourceUri,
            contentPath,
            metadataPath,
            hadCachedContent,
            cancellationToken);
    }

    private static async Task<CachedHttpContent?> TryReuseCachedContentAsync(
        HttpClient httpClient,
        Uri resourceUri,
        string contentPath,
        string metadataPath,
        HttpCacheMetadata? metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage request = CreateRequest(resourceUri, metadata);
            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return await ReadCachedContentAsync(contentPath, metadataPath, changed: false, cancellationToken);
            }

            response.EnsureSuccessStatusCode();
            return await WriteResponseAsync(response, contentPath, metadataPath, changed: true, cancellationToken);
        }
        catch (HttpRequestException) when (File.Exists(contentPath))
        {
            return await ReadCachedContentAsync(contentPath, metadataPath, changed: false, cancellationToken);
        }
    }

    private static async Task<CachedHttpContent> DownloadContentAsync(
        HttpClient httpClient,
        Uri resourceUri,
        string contentPath,
        string metadataPath,
        bool changed,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            resourceUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await WriteResponseAsync(response, contentPath, metadataPath, changed, cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(Uri resourceUri, HttpCacheMetadata? metadata)
    {
        HttpRequestMessage request = new(HttpMethod.Get, resourceUri)
        {
            Version = HttpVersion.Version11,
        };

        if (metadata is not null)
        {
            if (EntityTagHeaderValue.TryParse(metadata.ETag, out EntityTagHeaderValue? etag))
            {
                request.Headers.IfNoneMatch.Add(etag);
            }

            if (metadata.LastModifiedUtc is not null)
            {
                request.Headers.IfModifiedSince = metadata.LastModifiedUtc;
            }
        }

        return request;
    }

    private static async Task<CachedHttpContent> WriteResponseAsync(
        HttpResponseMessage response,
        string contentPath,
        string metadataPath,
        bool changed,
        CancellationToken cancellationToken)
    {
        string temporaryContentPath = $"{contentPath}.{Guid.NewGuid():N}.tmp";
        string temporaryMetadataPath = $"{metadataPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            byte[] contentBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            await File.WriteAllBytesAsync(temporaryContentPath, contentBytes, cancellationToken);
            File.Move(temporaryContentPath, contentPath, overwrite: true);

            HttpCacheMetadata metadata = HttpCacheMetadata.FromResponse(response);
            await WriteMetadataAsync(metadataPath, temporaryMetadataPath, metadata, cancellationToken);
            return new CachedHttpContent(contentBytes, metadata, changed);
        }
        catch
        {
            TryDeleteFile(temporaryContentPath);
            TryDeleteFile(temporaryMetadataPath);
            throw;
        }
    }

    private static async Task<CachedHttpContent> ReadCachedContentAsync(
        string contentPath,
        string metadataPath,
        bool changed,
        CancellationToken cancellationToken)
    {
        byte[] contentBytes = await File.ReadAllBytesAsync(contentPath, cancellationToken);
        HttpCacheMetadata? metadata = await TryReadMetadataAsync(metadataPath, cancellationToken);
        return new CachedHttpContent(contentBytes, metadata, changed);
    }

    private static async Task<HttpCacheMetadata?> TryReadMetadataAsync(
        string metadataPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            await using FileStream metadataFile = new(
                metadataPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                useAsync: true);
            return await JsonSerializer.DeserializeAsync<HttpCacheMetadata>(metadataFile, JsonOptions, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task WriteMetadataAsync(
        string metadataPath,
        string temporaryMetadataPath,
        HttpCacheMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using (FileStream metadataFile = new(
            temporaryMetadataPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            useAsync: true))
        {
            await JsonSerializer.SerializeAsync(metadataFile, metadata, JsonOptions, cancellationToken);
            await metadataFile.FlushAsync(cancellationToken);
        }

        try
        {
            File.Move(temporaryMetadataPath, metadataPath, overwrite: true);
        }
        catch
        {
            TryDeleteFile(temporaryMetadataPath);
            throw;
        }
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        File.Delete(path);
    }
}

internal sealed record CachedHttpContent(
    byte[] ContentBytes,
    HttpCacheMetadata? Metadata,
    bool Changed);

internal sealed record HttpCacheMetadata(
    string? ETag,
    DateTimeOffset? LastModifiedUtc,
    string? ContentType)
{
    public static HttpCacheMetadata FromResponse(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new HttpCacheMetadata(
            response.Headers.ETag?.Tag,
            response.Content.Headers.LastModified,
            response.Content.Headers.ContentType?.MediaType);
    }
}
