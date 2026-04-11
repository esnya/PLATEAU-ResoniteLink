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
