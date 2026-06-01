using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Domain.Importing;

public abstract record TerrainTextureSource
{
    public abstract string IdentityKey { get; }
}

public sealed record TerrainTextureTileSource(string UrlTemplate, int ZoomLevel) : TerrainTextureSource
{
    public string UrlTemplate { get; init; } = string.IsNullOrWhiteSpace(UrlTemplate)
        ? throw new ArgumentException("Terrain texture tile URL template must be provided.", nameof(UrlTemplate))
        : UrlTemplate;

    public int ZoomLevel { get; init; } = ZoomLevel is > 0 and <= WebMercatorTileMath.MaxZoomLevel
        ? ZoomLevel
        : throw new ArgumentOutOfRangeException(nameof(ZoomLevel));

    public override string IdentityKey =>
        string.Create(CultureInfo.InvariantCulture, $"tile-z{ZoomLevel}-{UrlTemplate}");
}

public sealed record GeoReferencedRasterMetadata(
    GeographicRectangle GeographicBounds,
    string? CoordinateSystemIdentifier,
    double PixelWidthMeters,
    double PixelHeightMeters)
{
    public bool IsUsable => !string.IsNullOrWhiteSpace(CoordinateSystemIdentifier)
        && PixelWidthMeters > 0.0
        && PixelHeightMeters > 0.0;

    public string IdentityKey =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"georaster-meta-crs-{CoordinateSystemIdentifier ?? "none"}-pixel-{TerrainTextureDescriptorFormatting.FormatRounded(PixelWidthMeters)}x{TerrainTextureDescriptorFormatting.FormatRounded(PixelHeightMeters)}-bounds-{TerrainTextureDescriptorFormatting.FormatBounds(GeographicBounds)}");
}

public interface ITerrainTextureRasterContentSource
{
    string IdentityKey { get; }

    string Description { get; }

    ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken);
}

public sealed record LocalTerrainTextureRasterContentSource(string SourcePath) : ITerrainTextureRasterContentSource
{
    public string SourcePath { get; init; } = string.IsNullOrWhiteSpace(SourcePath)
        ? throw new ArgumentException("Terrain texture raster source path must be provided.", nameof(SourcePath))
        : SourcePath;

    public string IdentityKey => $"file:{Path.GetFullPath(SourcePath)}";

    public string Description => Path.GetFileName(SourcePath.Replace('\\', '/'));

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The caller owns the returned stream and disposes it after raster decoding.")]
    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<Stream>(File.OpenRead(SourcePath));
    }
}

public sealed record TerrainTextureGeoReferencedRasterSource(
    ITerrainTextureRasterContentSource ContentSource,
    GeoReferencedRasterMetadata? Metadata = null) : TerrainTextureSource
{
    public TerrainTextureGeoReferencedRasterSource(
        string sourcePath,
        GeoReferencedRasterMetadata? metadata = null)
        : this(new LocalTerrainTextureRasterContentSource(sourcePath), metadata)
    {
    }

    public ITerrainTextureRasterContentSource ContentSource { get; init; } =
        ContentSource ?? throw new ArgumentNullException(nameof(ContentSource));

    public GeoReferencedRasterMetadata? Metadata { get; init; } = Metadata;

    public override string IdentityKey =>
        string.Create(CultureInfo.InvariantCulture, $"georaster-{ContentSource.IdentityKey}-meta-{Metadata?.IdentityKey ?? "none"}");

    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken) =>
        ContentSource.OpenReadAsync(cancellationToken);
}

public sealed record TerrainTextureOverlay
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Style",
        "IDE0032:Use auto property",
        Justification = "The init setter validates with-expression updates before changing overlay identity state.")]
    private string packageName = "";

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Style",
        "IDE0032:Use auto property",
        Justification = "The init setter validates with-expression updates before changing overlay identity state.")]
    private int maxTextureSize;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Style",
        "IDE0032:Use auto property",
        Justification = "The init setter snapshots and rejects null source elements for with-expression updates.")]
    private IReadOnlyList<TerrainTextureSource> sources = [];

    public TerrainTextureOverlay(
        string PackageName,
        ThirdRegionalMeshCode MeshCode,
        GeographicRectangle GeographicBounds,
        int MaxTextureSize,
        IReadOnlyList<TerrainTextureSource> Sources,
        TerrainTextureLicenseMode LicenseMode = TerrainTextureLicenseMode.Unknown)
    {
        this.PackageName = PackageName;
        this.MeshCode = MeshCode;
        this.GeographicBounds = GeographicBounds;
        this.MaxTextureSize = MaxTextureSize;
        this.Sources = Sources;
        this.LicenseMode = LicenseMode;
    }

    public TerrainTextureOverlay(
        string PackageName,
        ThirdRegionalMeshCode MeshCode,
        GeographicRectangle GeographicBounds,
        int MaxTextureSize,
        TerrainTextureSource PrimarySource,
        TerrainTextureSource? FallbackSource = null,
        TerrainTextureLicenseMode LicenseMode = TerrainTextureLicenseMode.Unknown)
        : this(
            PackageName,
            MeshCode,
            GeographicBounds,
            MaxTextureSize,
            FallbackSource is null ? [PrimarySource] : [PrimarySource, FallbackSource],
            LicenseMode)
    {
    }

    public TerrainTextureOverlay(
        string PackageName,
        ThirdRegionalMeshCode MeshCode,
        string UrlTemplate,
        int ZoomLevel,
        GeographicRectangle GeographicBounds,
        int MaxTextureSize,
        string? FallbackUrlTemplate = null,
        TerrainTextureLicenseMode LicenseMode = TerrainTextureLicenseMode.Unknown)
        : this(
            PackageName,
            MeshCode,
            GeographicBounds,
            MaxTextureSize,
            string.IsNullOrWhiteSpace(FallbackUrlTemplate)
                ? [new TerrainTextureTileSource(UrlTemplate, ZoomLevel)]
                : [new TerrainTextureTileSource(UrlTemplate, ZoomLevel), new TerrainTextureTileSource(FallbackUrlTemplate, ZoomLevel)],
            LicenseMode)
    {
    }

    public string PackageName
    {
        get => packageName;
        init => packageName = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Terrain texture package name must be provided.", nameof(value))
            : value.ToLowerInvariant();
    }

    public ThirdRegionalMeshCode MeshCode { get; init; }

    public GeographicRectangle GeographicBounds { get; init; }

    public int MaxTextureSize
    {
        get => maxTextureSize;
        init => maxTextureSize = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }

    public IReadOnlyList<TerrainTextureSource> Sources
    {
        get => sources;
        init => sources = CreateSourceSnapshot(value);
    }

    public TerrainTextureLicenseMode LicenseMode { get; init; }

    public TerrainTextureSource PrimarySource => Sources[0];

    public TerrainTextureSource? FallbackSource => Sources.Count > 1 ? Sources[1] : null;

    public string UrlTemplate => GetRequiredPrimaryTileSource().UrlTemplate;

    public int ZoomLevel => GetRequiredPrimaryTileSource().ZoomLevel;

    public string? FallbackUrlTemplate => GetFallbackTileSource()?.UrlTemplate;

    public int? FallbackZoomLevel => GetFallbackTileSource()?.ZoomLevel;

    public string SourceDescriptorKey =>
        string.Join("|", Sources.Select(static source => source.IdentityKey));

    public TerrainTextureTileSource GetRequiredPrimaryTileSource() => GetRequiredTileSource(PrimarySource);

    public TerrainTextureTileSource? GetFallbackTileSource() => FallbackSource as TerrainTextureTileSource;

    public IEnumerable<TerrainTextureTileSource> EnumerateTileSources() =>
        Sources.OfType<TerrainTextureTileSource>();

    public IEnumerable<TerrainTextureGeoReferencedRasterSource> EnumerateGeoReferencedRasterSources() =>
        Sources.OfType<TerrainTextureGeoReferencedRasterSource>();

    public bool Equals(TerrainTextureOverlay? other)
    {
        return ReferenceEquals(this, other)
            || (other is not null
                && string.Equals(PackageName, other.PackageName, StringComparison.Ordinal)
                && MeshCode.Equals(other.MeshCode)
                && GeographicBounds.Equals(other.GeographicBounds)
                && MaxTextureSize == other.MaxTextureSize
                && LicenseMode == other.LicenseMode
                && Sources.SequenceEqual(other.Sources));
    }

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(PackageName, StringComparer.Ordinal);
        hash.Add(MeshCode);
        hash.Add(GeographicBounds);
        hash.Add(MaxTextureSize);
        hash.Add((int)LicenseMode);
        foreach (TerrainTextureSource source in Sources)
        {
            hash.Add(source);
        }

        return hash.ToHashCode();
    }

    private static TerrainTextureTileSource GetRequiredTileSource(TerrainTextureSource source)
    {
        return source as TerrainTextureTileSource
            ?? throw new InvalidOperationException(
                $"Terrain texture source '{source.GetType().Name}' does not provide a web tile URL.");
    }

    private static ReadOnlyCollection<TerrainTextureSource> CreateSourceSnapshot(
        IReadOnlyList<TerrainTextureSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (sources.Count == 0)
        {
            throw new ArgumentException("At least one terrain texture source must be provided.", nameof(sources));
        }

        TerrainTextureSource[] snapshot = new TerrainTextureSource[sources.Count];
        for (int index = 0; index < sources.Count; index++)
        {
            snapshot[index] = sources[index]
                ?? throw new ArgumentException("Terrain texture sources cannot contain null.", nameof(sources));
        }

        return Array.AsReadOnly(snapshot);
    }
}

internal static class TerrainTextureDescriptorFormatting
{
    public static string FormatBounds(GeographicRectangle bounds) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{FormatRounded(bounds.MinLatitude)}-{FormatRounded(bounds.MaxLatitude)}-{FormatRounded(bounds.MinLongitude)}-{FormatRounded(bounds.MaxLongitude)}");

    public static string FormatRounded(double value)
    {
        double normalized = value == 0.0 ? 0.0 : value;
        return normalized.ToString("G17", CultureInfo.InvariantCulture);
    }
}

public enum TerrainTextureLicenseMode
{
    Unknown = 0,
    PlateauOrthoOnly = 1,
    PlateauOrthoWithGsiFallback = 2,
}
