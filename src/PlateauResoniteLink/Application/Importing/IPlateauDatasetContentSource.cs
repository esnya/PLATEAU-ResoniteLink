using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal interface IPlateauDatasetContentSource
{
    string SourcePath { get; }

    IReadOnlyList<string> EnumerateFiles();

    bool FileExists(string relativePath);

    string? ResolveRelativePath(string baseRelativePath, string candidatePath);

    ValueTask<Stream> OpenReadAsync(
        string relativePath,
        CancellationToken cancellationToken = default);

    Task<string> EnsureLocalFileAsync(
        string relativePath,
        string outputRoot,
        CancellationToken cancellationToken = default);
}

internal interface IPlateauDatasetContentLengthSource
{
    long? TryGetFileLength(string relativePath);
}
