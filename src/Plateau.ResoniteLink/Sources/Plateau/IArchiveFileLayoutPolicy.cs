namespace Plateau.ResoniteLink.Application.Importing;

public interface IArchiveFileLayoutPolicy
{
    bool IsSupportedArchivePath(string path);
    string CreateSafePathSegment(string value);
    string ResolveDatasetRoot(string workRoot, string dataset);
    string GetMaterializedArchiveRoot(string outputRoot, string archivePath);
    string GetMaterializedArchiveCacheKey(string archivePath);
    string NormalizeRelativePath(string path);
    string CombineRelativePaths(params string?[] segments);
    string GetDirectoryPath(string relativePath);
    string? ResolveRelativePath(string baseRelativePath, string candidatePath);
    string ResolveDatasetRootPrefix(IEnumerable<string> relativePaths);
    string StripDatasetRootPrefix(string relativePath, string datasetRootPrefix);
    string GetNestedArchivePrefix(string prefix, string entryKey);
}
