using System.Xml.Linq;

using GeographicLib;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed record SourceFileDescriptor(
    string RelativePath,
    string PackageName,
    string MatchedMeshCode,
    bool RequiresMeshAreaFilter)
{
    internal LocalCityGmlResonitePlanBuilder.SourceFileDescriptor ToLegacy()
    {
        return new LocalCityGmlResonitePlanBuilder.SourceFileDescriptor(
            RelativePath,
            PackageName,
            MatchedMeshCode,
            RequiresMeshAreaFilter);
    }

    internal static SourceFileDescriptor FromLegacy(LocalCityGmlResonitePlanBuilder.SourceFileDescriptor sourceFile)
    {
        return new SourceFileDescriptor(
            sourceFile.RelativePath,
            sourceFile.PackageName,
            sourceFile.MatchedMeshCode,
            sourceFile.RequiresMeshAreaFilter);
    }
}

internal sealed record CachedSourceFileDescriptor(
    SourceFileDescriptor SourceFile,
    BootstrapParsedCityObject[] CityObjects)
{
    public string RelativePath => SourceFile.RelativePath;

    public string PackageName => SourceFile.PackageName;

    internal LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor ToLegacy()
    {
        return new LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor(
            SourceFile.ToLegacy(),
            CityObjects.Select(static cityObject => cityObject.ToLegacy()).ToArray());
    }

    internal static CachedSourceFileDescriptor FromLegacy(LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor sourceFile)
    {
        return new CachedSourceFileDescriptor(
            SourceFileDescriptor.FromLegacy(sourceFile.SourceFile),
            sourceFile.CityObjects.Select(BootstrapParsedCityObject.FromLegacy).ToArray());
    }
}

internal sealed class SourceFilePipeline
{
    private readonly object parseTaskGate = new();
    private readonly Func<Task<ParsedSourceFileResult>> parseTaskFactory;
    private readonly LocalCityGmlResonitePlanBuilder.SourceFilePipeline? legacy;
    private Task<ParsedSourceFileResult>? parseTask;

    internal SourceFilePipeline(SourceFileDescriptor sourceFile, Func<Task<ParsedSourceFileResult>> parseTaskFactory)
    {
        SourceFile = sourceFile;
        this.parseTaskFactory = parseTaskFactory;
    }

    internal SourceFilePipeline(LocalCityGmlResonitePlanBuilder.SourceFilePipeline legacy)
        : this(
            SourceFileDescriptor.FromLegacy(legacy.SourceFile),
            async () => ParsedSourceFileResult.FromLegacy(await legacy.GetParseTask().ConfigureAwait(false)))
    {
        this.legacy = legacy;
    }

    public SourceFileDescriptor SourceFile { get; }

    public Task<ParsedSourceFileResult> GetParseTask()
    {
        lock (parseTaskGate)
        {
            parseTask ??= parseTaskFactory();
            return parseTask;
        }
    }

    internal LocalCityGmlResonitePlanBuilder.SourceFilePipeline ToLegacy()
    {
        return legacy ?? new LocalCityGmlResonitePlanBuilder.SourceFilePipeline(
            SourceFile.ToLegacy(),
            async () => (await GetParseTask().ConfigureAwait(false)).ToLegacy());
    }
}

internal sealed record ParsedSourceFileResult(
    SourceFileDescriptor SourceFile,
    BootstrapParsedCityObject[] CityObjects,
    CoordinateReferenceSystem? ReferenceSystem,
    TerrainHeightTriangle[] TerrainTriangles,
    TimeSpan Elapsed)
{
    internal LocalCityGmlResonitePlanBuilder.ParsedSourceFileResult ToLegacy()
    {
        return new LocalCityGmlResonitePlanBuilder.ParsedSourceFileResult(
            SourceFile.ToLegacy(),
            CityObjects.Select(static cityObject => cityObject.ToLegacy()).ToArray(),
            ReferenceSystem?.ToLegacy(),
            TerrainTriangles.Select(static triangle => triangle.ToLegacy()).ToArray(),
            Elapsed);
    }

    internal static ParsedSourceFileResult FromLegacy(LocalCityGmlResonitePlanBuilder.ParsedSourceFileResult sourceFile)
    {
        return new ParsedSourceFileResult(
            SourceFileDescriptor.FromLegacy(sourceFile.SourceFile),
            sourceFile.CityObjects.Select(BootstrapParsedCityObject.FromLegacy).ToArray(),
            sourceFile.ReferenceSystem is null ? null : CoordinateReferenceSystem.FromLegacy(sourceFile.ReferenceSystem),
            sourceFile.TerrainTriangles.Select(TerrainHeightTriangle.FromLegacy).ToArray(),
            sourceFile.Elapsed);
    }
}

internal sealed record GeodeticPoint(
    double Latitude,
    double Longitude,
    double Altitude)
{
    internal LocalCityGmlResonitePlanBuilder.GeodeticPoint ToLegacy()
    {
        return new LocalCityGmlResonitePlanBuilder.GeodeticPoint(Latitude, Longitude, Altitude);
    }

    internal static GeodeticPoint FromLegacy(LocalCityGmlResonitePlanBuilder.GeodeticPoint point)
    {
        return new GeodeticPoint(point.Latitude, point.Longitude, point.Altitude);
    }
}

internal sealed record TerrainHeightTriangle(
    GeodeticPoint Vertex0,
    GeodeticPoint Vertex1,
    GeodeticPoint Vertex2)
{
    internal LocalCityGmlResonitePlanBuilder.TerrainHeightTriangle ToLegacy()
    {
        return new LocalCityGmlResonitePlanBuilder.TerrainHeightTriangle(
            Vertex0.ToLegacy(),
            Vertex1.ToLegacy(),
            Vertex2.ToLegacy());
    }

    internal static TerrainHeightTriangle FromLegacy(LocalCityGmlResonitePlanBuilder.TerrainHeightTriangle triangle)
    {
        return new TerrainHeightTriangle(
            GeodeticPoint.FromLegacy(triangle.Vertex0),
            GeodeticPoint.FromLegacy(triangle.Vertex1),
            GeodeticPoint.FromLegacy(triangle.Vertex2));
    }
}

internal sealed record CoordinateReferenceSystem(
    string SrsName,
    Geocentric? Geocentric,
    string CompatibilityKey)
{
    private static readonly XNamespace Gml = "http://www.opengis.net/gml";

    public bool IsGeographic => Geocentric is not null;

    public bool IsCompatibleWith(CoordinateReferenceSystem other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return string.Equals(CompatibilityKey, other.CompatibilityKey, StringComparison.Ordinal);
    }

    public static CoordinateReferenceSystem Parse(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        string? srsName = document
            .Descendants(Gml + "Envelope")
            .Attributes("srsName")
            .Select(static attribute => attribute.Value.Trim())
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

        return Parse(srsName);
    }

    public static CoordinateReferenceSystem Parse(string? srsName)
    {
        if (string.IsNullOrWhiteSpace(srsName))
        {
            return new CoordinateReferenceSystem("local-cartesian", null, "local-cartesian");
        }

        (Geocentric geocentric, string compatibilityKey) = ResolveGeocentric(srsName);
        return new CoordinateReferenceSystem(srsName, geocentric, compatibilityKey);
    }

    internal static CoordinateReferenceSystem FromLegacy(LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem referenceSystem)
    {
        return new CoordinateReferenceSystem(referenceSystem.SrsName, referenceSystem.Geocentric, referenceSystem.CompatibilityKey);
    }

    internal LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem ToLegacy()
    {
        return new LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem(
            SrsName,
            Geocentric,
            CompatibilityKey);
    }

    private static (Geocentric Geocentric, string CompatibilityKey) ResolveGeocentric(string srsName)
    {
        if (srsName.EndsWith("/6697", StringComparison.Ordinal)
            || srsName.EndsWith("EPSG:6697", StringComparison.OrdinalIgnoreCase)
            || srsName.EndsWith("/6668", StringComparison.Ordinal)
            || srsName.EndsWith("EPSG:6668", StringComparison.OrdinalIgnoreCase))
        {
            return (new Geocentric(Ellipsoid.GRS80), "jgd2011");
        }

        if (srsName.EndsWith("/6696", StringComparison.Ordinal)
            || srsName.EndsWith("EPSG:6696", StringComparison.OrdinalIgnoreCase))
        {
            return (new Geocentric(Ellipsoid.GRS80), "jgd2000");
        }

        return (new Geocentric(Ellipsoid.GRS80), srsName.Trim());
    }
}

internal sealed record BootstrapParsedRing(
    string RingId,
    GeodeticPoint[] Vertices,
    IReadOnlyList<ResoniteFloat2>? UVs)
{
    internal LocalCityGmlResonitePlanBuilder.ParsedRing ToLegacy()
    {
        return new LocalCityGmlResonitePlanBuilder.ParsedRing(
            RingId,
            Vertices.Select(static point => point.ToLegacy()).ToArray(),
            UVs);
    }

    internal static BootstrapParsedRing FromLegacy(LocalCityGmlResonitePlanBuilder.ParsedRing ring)
    {
        return new BootstrapParsedRing(
            ring.RingId,
            ring.Vertices.Select(GeodeticPoint.FromLegacy).ToArray(),
            ring.UVs);
    }
}

internal enum BootstrapParsedSurfaceSemantic
{
    Unknown = 0,
    Wall = 1,
    Roof = 2,
    Ground = 3,
    Closure = 4,
    OuterCeiling = 5,
    OuterFloor = 6,
}

internal sealed record BootstrapParsedSurface(
    string PolygonId,
    BootstrapParsedSurfaceSemantic Semantic,
    BootstrapParsedRing ExteriorRing,
    BootstrapParsedRing[] InteriorRings,
    ResoniteColor BaseColor,
    string? TexturePath)
{
    public IEnumerable<GeodeticPoint> Vertices =>
        ExteriorRing.Vertices.Concat(InteriorRings.SelectMany(static ring => ring.Vertices));

    internal LocalCityGmlResonitePlanBuilder.ParsedSurface ToLegacy()
    {
        return new LocalCityGmlResonitePlanBuilder.ParsedSurface(
            PolygonId,
            (LocalCityGmlResonitePlanBuilder.ParsedSurfaceSemantic)Semantic,
            ExteriorRing.ToLegacy(),
            InteriorRings.Select(static ring => ring.ToLegacy()).ToArray(),
            BaseColor,
            TexturePath);
    }

    internal static BootstrapParsedSurface FromLegacy(LocalCityGmlResonitePlanBuilder.ParsedSurface surface)
    {
        return new BootstrapParsedSurface(
            surface.PolygonId,
            (BootstrapParsedSurfaceSemantic)surface.Semantic,
            BootstrapParsedRing.FromLegacy(surface.ExteriorRing),
            surface.InteriorRings.Select(BootstrapParsedRing.FromLegacy).ToArray(),
            surface.BaseColor,
            surface.TexturePath);
    }
}

internal sealed record BootstrapParsedCityObject(
    string SlotKey,
    string DisplayName,
    string PackageName,
    string ActualMeshCode,
    int? LodLevel,
    BootstrapParsedSurface[] Surfaces,
    CoordinateReferenceSystem ReferenceSystem,
    string SourceIdentity,
    bool SharedAcrossMeshCodes,
    bool TerrainAligned = false,
    GeodeticPoint? OriginOverride = null)
{
    internal LocalCityGmlResonitePlanBuilder.ParsedCityObject ToLegacy()
    {
        return new LocalCityGmlResonitePlanBuilder.ParsedCityObject(
            SlotKey,
            DisplayName,
            PackageName,
            ActualMeshCode,
            LodLevel,
            Surfaces.Select(static surface => surface.ToLegacy()).ToArray(),
            ReferenceSystem.ToLegacy(),
            SourceIdentity,
            SharedAcrossMeshCodes,
            TerrainAligned,
            OriginOverride?.ToLegacy());
    }

    internal static BootstrapParsedCityObject FromLegacy(LocalCityGmlResonitePlanBuilder.ParsedCityObject cityObject)
    {
        return new BootstrapParsedCityObject(
            cityObject.SlotKey,
            cityObject.DisplayName,
            cityObject.PackageName,
            cityObject.ActualMeshCode,
            cityObject.LodLevel,
            cityObject.Surfaces.Select(BootstrapParsedSurface.FromLegacy).ToArray(),
            CoordinateReferenceSystem.FromLegacy(cityObject.ReferenceSystem),
            cityObject.SourceIdentity,
            cityObject.SharedAcrossMeshCodes,
            cityObject.TerrainAligned,
            cityObject.OriginOverride is null ? null : GeodeticPoint.FromLegacy(cityObject.OriginOverride));
    }
}

internal sealed class TerrainHeightSampler
{
    private readonly LocalCityGmlResonitePlanBuilder.TerrainHeightSampler legacy;

    internal TerrainHeightSampler(LocalCityGmlResonitePlanBuilder.TerrainHeightSampler legacy)
    {
        this.legacy = legacy;
    }

    internal static TerrainHeightSampler? FromLegacy(LocalCityGmlResonitePlanBuilder.TerrainHeightSampler? terrainHeightSampler)
    {
        return terrainHeightSampler is null ? null : new TerrainHeightSampler(terrainHeightSampler);
    }

    internal LocalCityGmlResonitePlanBuilder.TerrainHeightSampler ToLegacy()
    {
        return legacy;
    }

    internal static TerrainHeightSampler Create(
        IReadOnlyCollection<TerrainHeightTriangle> terrainTriangles,
        GeodeticPoint globalOriginPoint,
        Geocentric geocentric)
    {
        return new TerrainHeightSampler(
            LocalCityGmlResonitePlanBuilder.TerrainHeightSampler.Create(
                terrainTriangles.Select(static triangle => triangle.ToLegacy()).ToArray(),
                globalOriginPoint.ToLegacy(),
                geocentric));
    }
}
