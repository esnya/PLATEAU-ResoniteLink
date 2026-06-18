using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing.Contracts;

namespace PlateauResoniteLink.Application.Importing;

internal interface IPlateauDatasetContentSource : ITextureContentSource
{
    string SourcePath { get; }

    IReadOnlyList<string> EnumerateFiles();

    bool FileExists(string relativePath);

    string? ResolveRelativePath(string baseRelativePath, string candidatePath);

    Task<string> EnsureLocalFileAsync(
        string relativePath,
        string outputRoot,
        CancellationToken cancellationToken = default);
}
