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

    public int ZoomLevel { get; init; } = ZoomLevel > 0
        ? ZoomLevel
        : throw new ArgumentOutOfRangeException(nameof(ZoomLevel));

    public override string IdentityKey =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"tile|{ZoomLevel}|{UrlTemplate}");
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
            System.Globalization.CultureInfo.InvariantCulture,
            $"{CoordinateSystemIdentifier ?? "unknown"}|{PixelWidthMeters:R}|{PixelHeightMeters:R}|"
            + $"{GeographicBounds.MinLatitude:R}|{GeographicBounds.MaxLatitude:R}|"
            + $"{GeographicBounds.MinLongitude:R}|{GeographicBounds.MaxLongitude:R}");
}

public sealed record TerrainTextureGeoReferencedRasterSource(
    string SourcePath,
    GeoReferencedRasterMetadata? Metadata = null) : TerrainTextureSource
{
    public string SourcePath { get; init; } = string.IsNullOrWhiteSpace(SourcePath)
        ? throw new ArgumentException("Terrain texture raster source path must be provided.", nameof(SourcePath))
        : SourcePath;

    public GeoReferencedRasterMetadata? Metadata { get; init; } = Metadata;

    public override string IdentityKey =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"georaster|{SourcePath}|{Metadata?.IdentityKey ?? "unresolved"}");
}

public sealed record TerrainTextureOverlay
{
    public TerrainTextureOverlay(
        string PackageName,
        GeographicRectangle GeographicBounds,
        int MaxTextureSize,
        IReadOnlyList<TerrainTextureSource> Sources,
        TerrainTextureLicenseMode LicenseMode = TerrainTextureLicenseMode.Unknown)
    {
        this.PackageName = string.IsNullOrWhiteSpace(PackageName)
            ? throw new ArgumentException("Terrain texture package name must be provided.", nameof(PackageName))
            : PackageName;
        this.GeographicBounds = GeographicBounds;
        this.MaxTextureSize = MaxTextureSize > 0
            ? MaxTextureSize
            : throw new ArgumentOutOfRangeException(nameof(MaxTextureSize));
        this.Sources = Sources is { Count: > 0 }
            ? Sources.ToArray()
            : throw new ArgumentException("At least one terrain texture source must be provided.", nameof(Sources));
        this.LicenseMode = LicenseMode;
    }

    public TerrainTextureOverlay(
        string PackageName,
        GeographicRectangle GeographicBounds,
        int MaxTextureSize,
        TerrainTextureSource PrimarySource,
        TerrainTextureSource? FallbackSource = null,
        TerrainTextureLicenseMode LicenseMode = TerrainTextureLicenseMode.Unknown)
        : this(
            PackageName,
            GeographicBounds,
            MaxTextureSize,
            FallbackSource is null ? [PrimarySource] : [PrimarySource, FallbackSource],
            LicenseMode)
    {
    }

    public TerrainTextureOverlay(
        string PackageName,
        string UrlTemplate,
        int ZoomLevel,
        GeographicRectangle GeographicBounds,
        int MaxTextureSize,
        string? FallbackUrlTemplate = null,
        TerrainTextureLicenseMode LicenseMode = TerrainTextureLicenseMode.Unknown)
        : this(
            PackageName,
            GeographicBounds,
            MaxTextureSize,
            string.IsNullOrWhiteSpace(FallbackUrlTemplate)
                ? [new TerrainTextureTileSource(UrlTemplate, ZoomLevel)]
                : [new TerrainTextureTileSource(UrlTemplate, ZoomLevel), new TerrainTextureTileSource(FallbackUrlTemplate, ZoomLevel)],
            LicenseMode)
    {
    }

    public string PackageName { get; init; }

    public GeographicRectangle GeographicBounds { get; init; }

    public int MaxTextureSize { get; init; }

    public IReadOnlyList<TerrainTextureSource> Sources { get; init; }

    public TerrainTextureLicenseMode LicenseMode { get; init; }

    public TerrainTextureSource PrimarySource => Sources[0];

    public TerrainTextureSource? FallbackSource => Sources.Count > 1 ? Sources[1] : null;

    public string UrlTemplate => GetRequiredPrimaryTileSource().UrlTemplate;

    public int ZoomLevel => GetRequiredPrimaryTileSource().ZoomLevel;

    public string? FallbackUrlTemplate => GetFallbackTileSource()?.UrlTemplate;

    public int? FallbackZoomLevel => GetFallbackTileSource()?.ZoomLevel;

    public string SourceIdentityKey =>
        string.Join(
            "|then|",
            Sources.Select(static source => source.IdentityKey));

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
                && GeographicBounds.Equals(other.GeographicBounds)
                && MaxTextureSize == other.MaxTextureSize
                && LicenseMode == other.LicenseMode
                && Sources.SequenceEqual(other.Sources));
    }

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(PackageName, StringComparer.Ordinal);
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
}

public enum TerrainTextureLicenseMode
{
    Unknown = 0,
    PlateauOrthoOnly = 1,
    PlateauOrthoWithGsiFallback = 2,
}
