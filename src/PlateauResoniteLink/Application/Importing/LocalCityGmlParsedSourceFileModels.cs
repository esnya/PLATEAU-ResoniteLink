using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record SourceFileDescriptor(
    string RelativePath,
    string PackageName,
    string MatchedMeshCode,
    bool RequiresMeshAreaFilter)
{
    internal LocalCityGmlObjectProjection.SourceFileDescriptor ToProjectionModel()
    {
        return new LocalCityGmlObjectProjection.SourceFileDescriptor(
            RelativePath,
            PackageName,
            MatchedMeshCode,
            RequiresMeshAreaFilter);
    }

    internal static SourceFileDescriptor FromProjectionModel(LocalCityGmlObjectProjection.SourceFileDescriptor sourceFile)
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
    ParsedCityObject[] CityObjects)
{
    public string RelativePath => SourceFile.RelativePath;

    public string PackageName => SourceFile.PackageName;

    internal LocalCityGmlObjectProjection.CachedSourceFileDescriptor ToProjectionModel()
    {
        return new LocalCityGmlObjectProjection.CachedSourceFileDescriptor(
            SourceFile.ToProjectionModel(),
            CityObjects.Select(static cityObject => cityObject.ToProjectionModel()).ToArray());
    }

    internal static CachedSourceFileDescriptor FromProjectionModel(LocalCityGmlObjectProjection.CachedSourceFileDescriptor sourceFile)
    {
        return new CachedSourceFileDescriptor(
            SourceFileDescriptor.FromProjectionModel(sourceFile.SourceFile),
            sourceFile.CityObjects.Select(ParsedCityObject.FromProjectionModel).ToArray());
    }
}

internal sealed class SourceFilePipeline
{
    private readonly object parseTaskGate = new();
    private readonly Func<Task<ParsedSourceFileResult>> parseTaskFactory;
    private readonly Func<CancellationToken, IAsyncEnumerable<ParsedCityObject>> streamFactory;
    private Task<ParsedSourceFileResult>? parseTask;

    internal SourceFilePipeline(
        SourceFileDescriptor sourceFile,
        Func<Task<ParsedSourceFileResult>> parseTaskFactory,
        Func<CancellationToken, IAsyncEnumerable<ParsedCityObject>>? streamFactory = null)
    {
        SourceFile = sourceFile;
        this.parseTaskFactory = parseTaskFactory;
        this.streamFactory = streamFactory ?? CreateParseTaskBackedStream;
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

    public IAsyncEnumerable<ParsedCityObject> StreamParsedCityObjectsAsync(
        CancellationToken cancellationToken = default)
    {
        return streamFactory(cancellationToken);
    }

    private async IAsyncEnumerable<ParsedCityObject> CreateParseTaskBackedStream(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ParsedSourceFileResult parsedSourceFile = await GetParseTask().WaitAsync(cancellationToken);
        foreach (ParsedCityObject cityObject in parsedSourceFile.CityObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return cityObject;
        }
    }
}

internal sealed record ParsedSourceFileResult(
    SourceFileDescriptor SourceFile,
    ParsedCityObject[] CityObjects,
    CoordinateReferenceSystem? ReferenceSystem,
    TerrainHeightTriangle[] TerrainTriangles,
    TimeSpan Elapsed)
{
    internal LocalCityGmlObjectProjection.ParsedSourceFileResult ToProjectionModel()
    {
        return new LocalCityGmlObjectProjection.ParsedSourceFileResult(
            SourceFile.ToProjectionModel(),
            CityObjects.Select(static cityObject => cityObject.ToProjectionModel()).ToArray(),
            ReferenceSystem?.ToProjectionModel(),
            TerrainTriangles.Select(static triangle => triangle.ToProjectionModel()).ToArray(),
            Elapsed);
    }

    internal static ParsedSourceFileResult FromProjectionModel(LocalCityGmlObjectProjection.ParsedSourceFileResult sourceFile)
    {
        return new ParsedSourceFileResult(
            SourceFileDescriptor.FromProjectionModel(sourceFile.SourceFile),
            sourceFile.CityObjects.Select(ParsedCityObject.FromProjectionModel).ToArray(),
            sourceFile.ReferenceSystem is null ? null : CoordinateReferenceSystem.FromProjectionModel(sourceFile.ReferenceSystem),
            sourceFile.TerrainTriangles.Select(TerrainHeightTriangle.FromProjectionModel).ToArray(),
            sourceFile.Elapsed);
    }
}
