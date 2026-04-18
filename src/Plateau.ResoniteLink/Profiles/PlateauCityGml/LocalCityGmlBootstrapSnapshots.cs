using GeographicLib;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed record LocalCityGmlDocumentBootstrapState(
    IReadOnlyList<string> RelativeSourceFiles,
    IReadOnlyList<string> PackageNames,
    IReadOnlyList<TerrainTextureOverlay> TerrainTextureOverlays,
    IReadOnlyList<string> RequestedMeshCodes,
    IReadOnlyList<LocalCityGmlCachedSourceFileSnapshot> CachedDemSourceFiles,
    LocalCityGmlReferenceSystemSnapshot ReferenceSystem,
    LocalCityGmlGeodeticPointSnapshot GlobalOriginPoint,
    LocalCityGmlTerrainHeightSamplerSnapshot? TerrainHeightSampler);

public sealed record LocalCityGmlCachedSourceFileSnapshot(
    LocalCityGmlSourceFileSnapshot SourceFile,
    int CityObjectCount)
{
    internal LocalCityGmlCachedSourceFileSnapshot(
        LocalCityGmlObjectProjection.CachedSourceFileDescriptor legacy)
        : this(new LocalCityGmlSourceFileSnapshot(legacy.SourceFile), legacy.CityObjects.Length)
    {
        Legacy = legacy;
    }

    internal LocalCityGmlObjectProjection.CachedSourceFileDescriptor? Legacy { get; }
}

public sealed record LocalCityGmlSourceFileSnapshot(
    string RelativePath,
    string PackageName,
    string MatchedMeshCode,
    bool RequiresMeshAreaFilter)
{
    internal LocalCityGmlSourceFileSnapshot(
        LocalCityGmlObjectProjection.SourceFileDescriptor legacy)
        : this(
            legacy.RelativePath,
            legacy.PackageName,
            legacy.MatchedMeshCode,
            legacy.RequiresMeshAreaFilter)
    {
        Legacy = legacy;
    }

    internal LocalCityGmlObjectProjection.SourceFileDescriptor? Legacy { get; }
}

public sealed record LocalCityGmlReferenceSystemSnapshot(
    string SrsName,
    bool IsGeographic,
    string CompatibilityKey)
{
    internal LocalCityGmlReferenceSystemSnapshot(
        LocalCityGmlObjectProjection.CoordinateReferenceSystem legacy)
        : this(legacy.SrsName, legacy.IsGeographic, legacy.CompatibilityKey)
    {
        Legacy = legacy;
    }

    internal LocalCityGmlObjectProjection.CoordinateReferenceSystem? Legacy { get; }
}

public sealed record LocalCityGmlGeodeticPointSnapshot(
    double Latitude,
    double Longitude,
    double Altitude)
{
    internal LocalCityGmlGeodeticPointSnapshot(
        LocalCityGmlObjectProjection.GeodeticPoint legacy)
        : this(legacy.Latitude, legacy.Longitude, legacy.Altitude)
    {
        Legacy = legacy;
    }

    internal LocalCityGmlObjectProjection.GeodeticPoint? Legacy { get; }
}

public sealed record LocalCityGmlTerrainHeightSamplerSnapshot
{
    internal LocalCityGmlTerrainHeightSamplerSnapshot(
        LocalCityGmlObjectProjection.TerrainHeightSampler legacy)
    {
        Legacy = legacy;
    }

    internal LocalCityGmlObjectProjection.TerrainHeightSampler? Legacy { get; }
}

public sealed record LocalCityGmlGeometryProjectionContext(
    LocalCityGmlCachedSourceFileSnapshot SourceFile,
    LocalCityGmlReferenceSystemSnapshot ReferenceSystem,
    LocalCityGmlGeodeticPointSnapshot GlobalOriginPoint,
    LocalCartesian? GlobalCartesian,
    IReadOnlyList<TerrainTextureOverlay> DemTerrainTextureOverlays,
    LocalCityGmlTerrainHeightSamplerSnapshot? TerrainHeightSampler);
