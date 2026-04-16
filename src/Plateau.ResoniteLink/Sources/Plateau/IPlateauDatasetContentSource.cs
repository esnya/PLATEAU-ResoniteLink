namespace Plateau.ResoniteLink.Application.Importing;

public interface IPlateauDatasetContentSource
{
    string SourcePath { get; }

    IReadOnlyList<string> EnumerateFiles();

    bool FileExists(string relativePath);

    ValueTask<Stream> OpenReadAsync(
        string relativePath,
        CancellationToken cancellationToken = default);

    Task<string> MaterializeFileAsync(
        string relativePath,
        string outputRoot,
        CancellationToken cancellationToken = default);
}
