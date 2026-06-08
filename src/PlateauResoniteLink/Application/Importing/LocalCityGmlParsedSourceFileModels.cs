using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record SourceFileDescriptor(
    string RelativePath,
    string PackageName,
    string MatchedMeshCode,
    bool RequiresMeshCodeBoundsFilter,
    string? SourceFileRootMeshCode = null)
{
    public string EffectiveSourceFileRootMeshCode => string.IsNullOrWhiteSpace(SourceFileRootMeshCode)
        ? MatchedMeshCode
        : SourceFileRootMeshCode!;
}

internal sealed record CachedSourceFileDescriptor(
    SourceFileDescriptor SourceFile,
    ParsedCityObject[] CityObjects,
    CoordinateReferenceSystem ReferenceSystem)
{
    public string RelativePath => SourceFile.RelativePath;

    public string PackageName => SourceFile.PackageName;
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
    CoordinateReferenceSystem ReferenceSystem,
    TerrainHeightTriangle[] TerrainTriangles,
    TimeSpan Elapsed);
