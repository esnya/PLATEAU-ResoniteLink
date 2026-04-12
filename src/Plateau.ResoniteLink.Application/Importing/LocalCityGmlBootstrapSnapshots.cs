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
        LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor legacy)
        : this(new LocalCityGmlSourceFileSnapshot(legacy.SourceFile), legacy.CityObjects.Length)
    {
        Legacy = legacy;
    }

    internal LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor? Legacy { get; }
}

public sealed record LocalCityGmlSourceFileSnapshot(
    string RelativePath,
    string PackageName,
    string MatchedMeshCode,
    bool RequiresMeshAreaFilter)
{
    internal LocalCityGmlSourceFileSnapshot(
        LocalCityGmlResonitePlanBuilder.SourceFileDescriptor legacy)
        : this(
            legacy.RelativePath,
            legacy.PackageName,
            legacy.MatchedMeshCode,
            legacy.RequiresMeshAreaFilter)
    {
        Legacy = legacy;
    }

    internal LocalCityGmlResonitePlanBuilder.SourceFileDescriptor? Legacy { get; }
}

public sealed record LocalCityGmlReferenceSystemSnapshot(
    string SrsName,
    bool IsGeographic,
    string CompatibilityKey)
{
    internal LocalCityGmlReferenceSystemSnapshot(
        LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem legacy)
        : this(legacy.SrsName, legacy.IsGeographic, legacy.CompatibilityKey)
    {
        Legacy = legacy;
    }

    internal LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem? Legacy { get; }
}

public sealed record LocalCityGmlGeodeticPointSnapshot(
    double Latitude,
    double Longitude,
    double Altitude)
{
    internal LocalCityGmlGeodeticPointSnapshot(
        LocalCityGmlResonitePlanBuilder.GeodeticPoint legacy)
        : this(legacy.Latitude, legacy.Longitude, legacy.Altitude)
    {
        Legacy = legacy;
    }

    internal LocalCityGmlResonitePlanBuilder.GeodeticPoint? Legacy { get; }
}

public sealed record LocalCityGmlTerrainHeightSamplerSnapshot
{
    internal LocalCityGmlTerrainHeightSamplerSnapshot(
        LocalCityGmlResonitePlanBuilder.TerrainHeightSampler legacy)
    {
        Legacy = legacy;
    }

    internal LocalCityGmlResonitePlanBuilder.TerrainHeightSampler? Legacy { get; }
}

public sealed record LocalCityGmlGeometryProjectionContext(
    LocalCityGmlCachedSourceFileSnapshot SourceFile,
    LocalCityGmlReferenceSystemSnapshot ReferenceSystem,
    LocalCityGmlGeodeticPointSnapshot GlobalOriginPoint,
    LocalCartesian? GlobalCartesian,
    IReadOnlyList<TerrainTextureOverlay> DemTerrainTextureOverlays,
    LocalCityGmlTerrainHeightSamplerSnapshot? TerrainHeightSampler);
