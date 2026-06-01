using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Application.Importing;

public enum RawTexturePayloadFormat
{
    Rgba32 = 0,
    RgbaFloat32 = 1,
}

public sealed record RawTexturePayload(
    int Width,
    int Height,
    string? ColorProfile,
    byte[] Bytes,
    RawTexturePayloadFormat Format = RawTexturePayloadFormat.Rgba32);

public interface ITextureImportSource
{
    string Identity { get; }

    string Description { get; }

    string? ColorProfile { get; }

    long? EstimatedByteLength { get; }
}

internal interface IRawTexturePayloadSource : ITextureImportSource
{
    ValueTask<RawTexturePayload> MaterializeRawAsync(CancellationToken cancellationToken);
}

internal static class TextureImportSourceMaterializer
{
    public static ValueTask<RawTexturePayload> MaterializeRawAsync(
        ITextureImportSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source is not IRawTexturePayloadSource rawSource)
        {
            throw new InvalidOperationException(
                $"Texture import source '{source.GetType().Name}' cannot materialize a raw texture payload.");
        }

        return rawSource.MaterializeRawAsync(cancellationToken);
    }
}

internal sealed class InMemoryRawTextureImportSource : IRawTexturePayloadSource
{
    private readonly byte[] bytes;

    public InMemoryRawTextureImportSource(
        int width,
        int height,
        string? colorProfile,
        byte[] bytes,
        string identity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        this.bytes = (byte[])bytes.Clone();
        Identity = identity;
    }

    public int Width { get; }

    public int Height { get; }

    public string Identity { get; }

    public string Description => $"memory:{Identity}";

    public string? ColorProfile { get; }

    public long? EstimatedByteLength => bytes.Length;

    public ValueTask<RawTexturePayload> MaterializeRawAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new RawTexturePayload(
            Width,
            Height,
            ColorProfile,
            (byte[])bytes.Clone()));
    }
}

internal sealed class InMemoryEncodedTextureImportSource : IRawTexturePayloadSource
{
    private readonly byte[] bytes;

    public InMemoryEncodedTextureImportSource(
        string? colorProfile,
        byte[] bytes,
        string identity)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ColorProfile = colorProfile;
        this.bytes = (byte[])bytes.Clone();
        Identity = identity;
    }

    public string Identity { get; }

    public string Description => $"memory:{Identity}";

    public string? ColorProfile { get; }

    public long? EstimatedByteLength => bytes.Length;

    public async ValueTask<RawTexturePayload> MaterializeRawAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using MemoryStream stream = new(bytes, writable: false);
        using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(stream, cancellationToken);
        return TextureImportSourceFactory.CreateRawPayloadFromImage(
            image,
            ColorProfile);
    }
}

internal sealed class DatasetTextureImportSource(
    IPlateauDatasetContentSource datasetSource,
    string relativePath,
    string? colorProfile,
    string identity) : IRawTexturePayloadSource
{
    public string Identity { get; } = identity;

    public string Description => $"dataset:{relativePath}";

    public string? ColorProfile { get; } = colorProfile;

    public long? EstimatedByteLength => datasetSource is IPlateauDatasetContentLengthSource lengthSource
        ? lengthSource.TryGetFileLength(relativePath)
        : null;

    public async ValueTask<RawTexturePayload> MaterializeRawAsync(CancellationToken cancellationToken)
    {
        await using Stream stream = await datasetSource.OpenReadAsync(relativePath, cancellationToken);
        using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(stream, cancellationToken);
        return TextureImportSourceFactory.CreateRawPayloadFromImage(image, ColorProfile);
    }
}

internal sealed class FileTextureImportSource(
    string absolutePath,
    string colorProfile,
    string identity) : IRawTexturePayloadSource
{
    public string Identity { get; } = identity;

    public string Description => $"file:{Path.GetFileName(absolutePath)}";

    public string? ColorProfile { get; } = colorProfile;

    public long? EstimatedByteLength
    {
        get
        {
            try
            {
                return File.Exists(absolutePath) ? new FileInfo(absolutePath).Length : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    public async ValueTask<RawTexturePayload> MaterializeRawAsync(CancellationToken cancellationToken)
    {
        using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(absolutePath, cancellationToken);
        return TextureImportSourceFactory.CreateRawPayloadFromImage(image, ColorProfile);
    }
}

internal sealed class GeneratedTextureImportSource(
    Func<CancellationToken, ValueTask<RawTexturePayload>> materializeRawAsync,
    string identity,
    string description,
    string? colorProfile,
    long? estimatedByteLength = null) : IRawTexturePayloadSource
{
    public string Identity { get; } = identity;

    public string Description { get; } = description;

    public string? ColorProfile { get; } = colorProfile;

    public long? EstimatedByteLength { get; } = estimatedByteLength;

    public ValueTask<RawTexturePayload> MaterializeRawAsync(CancellationToken cancellationToken)
    {
        return materializeRawAsync(cancellationToken);
    }
}

internal static class TextureImportSourceFactory
{
    public static ITextureImportSource CreateInMemoryRaw(
        int width,
        int height,
        string? colorProfile,
        byte[] bytes,
        string identity)
    {
        return new InMemoryRawTextureImportSource(
            width,
            height,
            colorProfile,
            bytes,
            identity);
    }

    public static ITextureImportSource CreateInMemoryEncodedImage(
        string? colorProfile,
        byte[] bytes,
        string identity)
    {
        return new InMemoryEncodedTextureImportSource(
            colorProfile,
            bytes,
            identity);
    }

    public static ITextureImportSource CreateDatasetEncodedImage(
        IPlateauDatasetContentSource datasetSource,
        string relativePath,
        string? colorProfile,
        string identity)
    {
        return new DatasetTextureImportSource(datasetSource, relativePath, colorProfile, identity);
    }

    public static ITextureImportSource CreateFileImage(
        string absolutePath,
        string colorProfile,
        string? identity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorProfile);
        return new FileTextureImportSource(
            absolutePath,
            colorProfile,
            identity ?? $"file:{Path.GetFullPath(absolutePath)}:{colorProfile}");
    }

    public static ITextureImportSource CreateGeneratedImage(
        Func<CancellationToken, ValueTask<RawTexturePayload>> materializeRawAsync,
        string identity,
        string description,
        string? colorProfile,
        long? estimatedByteLength = null)
    {
        return new GeneratedTextureImportSource(
            materializeRawAsync,
            identity,
            description,
            colorProfile,
            estimatedByteLength);
    }

    public static ITextureImportSource CreateGeneratedImageFromClone(
        Image<Rgba32> image,
        string identity,
        string description,
        string? colorProfile)
    {
        ArgumentNullException.ThrowIfNull(image);
        Image<Rgba32> retainedImage = image.Clone();
        object gate = new();
        return CreateGeneratedImage(
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (gate)
                {
                    return ValueTask.FromResult(CreateRawPayloadFromImage(retainedImage, colorProfile));
                }
            },
            identity,
            description,
            colorProfile,
            (long)image.Width * image.Height * 4);
    }

    public static RawTexturePayload CreateRawPayloadFromImage(
        Image<Rgba32> image,
        string? colorProfile)
    {
        ArgumentNullException.ThrowIfNull(image);
        byte[] rawBytes = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(rawBytes);
        return new RawTexturePayload(
            image.Width,
            image.Height,
            colorProfile,
            rawBytes);
    }
}
