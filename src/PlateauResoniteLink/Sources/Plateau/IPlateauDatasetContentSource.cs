namespace PlateauResoniteLink.Application.Importing;

public interface IPlateauDatasetContentSource
{
    string SourcePath { get; }

    IReadOnlyList<string> EnumerateFiles();

    bool FileExists(string relativePath);

    string? ResolveRelativePath(string baseRelativePath, string candidatePath)
    {
        return PlateauDatasetContentSourceFactory.ResolveRelativePath(
            baseRelativePath,
            candidatePath,
            new ArchiveFileLayoutPolicy());
    }

    ValueTask<Stream> OpenReadAsync(
        string relativePath,
        CancellationToken cancellationToken = default);

    Task<string> EnsureLocalFileAsync(
        string relativePath,
        string outputRoot,
        CancellationToken cancellationToken = default);
}
