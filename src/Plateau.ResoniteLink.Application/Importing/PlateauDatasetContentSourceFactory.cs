using SharpCompress.Archives;
using SharpCompress.Readers;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public static class PlateauDatasetContentSourceFactory
{
    public static async Task<IPlateauDatasetContentSource> CreateAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        string fullPath = Path.GetFullPath(sourcePath);
        if (Directory.Exists(fullPath))
        {
            return new LocalPlateauDatasetContentSource(PlateauDatasetPathResolver.ResolveDatasetRoot(fullPath));
        }

        if (!File.Exists(fullPath))
        {
            throw new PlateauImportValidationException([$"The local source path '{sourcePath}' does not exist."]);
        }

        if (!TryGetSupportedArchiveExtension(fullPath, out _))
        {
            throw new PlateauImportValidationException(
                [$"The local source path '{sourcePath}' is not a supported dataset directory or archive."]);
        }

        return await ArchivePlateauDatasetContentSource.CreateAsync(fullPath, cancellationToken);
    }

    private static bool TryGetSupportedArchiveExtension(string path, out string extension)
    {
        extension = Path.GetExtension(path);
        return string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }

    private static string NormalizeSafeRelativePath(string path)
    {
        string normalizedPath = NormalizeRelativePath(path);
        string[] segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(static segment => segment is "." or ".."))
        {
            throw new ArgumentException(
                $"The dataset relative path '{path}' contains path traversal segments.",
                nameof(path));
        }

        return normalizedPath;
    }

    internal static string CombineRelativePaths(params string?[] segments)
    {
        return string.Join(
            "/",
            segments
                .Where(static segment => !string.IsNullOrWhiteSpace(segment))
                .Select(static segment => NormalizeRelativePath(segment!))
                .Where(static segment => segment.Length > 0));
    }

    internal static string GetDirectoryPath(string relativePath)
    {
        string normalized = NormalizeRelativePath(relativePath);
        int separatorIndex = normalized.LastIndexOf('/');
        return separatorIndex < 0 ? string.Empty : normalized[..separatorIndex];
    }

    internal static string? ResolveRelativePath(string baseRelativePath, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return null;
        }

        string normalizedCandidate = candidatePath.Replace('\\', '/').Trim();
        if (normalizedCandidate.StartsWith('/'))
        {
            return null;
        }

        string baseDirectory = GetDirectoryPath(baseRelativePath);
        string combined = NormalizeRelativePath(Path.GetRelativePath(
            "/",
            Path.GetFullPath(Path.Combine("/", baseDirectory, normalizedCandidate))));
        return combined.StartsWith("..", StringComparison.Ordinal) ? null : combined;
    }

    private sealed class LocalPlateauDatasetContentSource(string datasetRoot) : IPlateauDatasetContentSource
    {
        public string SourcePath => datasetRoot;

        public IReadOnlyList<string> EnumerateFiles()
        {
            return Directory
                .EnumerateFiles(datasetRoot, "*", SearchOption.AllDirectories)
                .Select(path => NormalizeRelativePath(Path.GetRelativePath(datasetRoot, path)))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        public bool FileExists(string relativePath)
        {
            string absolutePath = Path.Combine(datasetRoot, NormalizeRelativePath(relativePath));
            return File.Exists(absolutePath);
        }

        public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CA2000
            return ValueTask.FromResult<Stream>(OpenFileReadStream(datasetRoot, relativePath));
#pragma warning restore CA2000
        }

        public Task<string> MaterializeFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Path.Combine(datasetRoot, NormalizeRelativePath(relativePath)));
        }

        private static FileStream OpenFileReadStream(string datasetRoot, string relativePath)
        {
            return new FileStream(
                Path.Combine(datasetRoot, NormalizeRelativePath(relativePath)),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                useAsync: true);
        }
    }

    private sealed class ArchivePlateauDatasetContentSource : IPlateauDatasetContentSource
    {
        private sealed record ArchiveFileAccessor(
            string RelativePath,
            Func<CancellationToken, ValueTask<Stream>> OpenReadAsync);

        private sealed record RawArchiveFileAccessor(
            string RelativePath,
            Func<CancellationToken, ValueTask<Stream>> OpenReadAsync);

        private string ArchivePath { get; }
        private IReadOnlyDictionary<string, ArchiveFileAccessor> Files { get; }

        private ArchivePlateauDatasetContentSource(
            string archivePath,
            IReadOnlyDictionary<string, ArchiveFileAccessor> files)
        {
            ArchivePath = archivePath;
            Files = files;
        }

        public string SourcePath => ArchivePath;

        public static async Task<ArchivePlateauDatasetContentSource> CreateAsync(
            string archivePath,
            CancellationToken cancellationToken)
        {
            List<RawArchiveFileAccessor> rawFiles = [];
            await IndexArchiveAsync(
                archivePath,
                ct => OpenArchiveFileStreamAsync(archivePath, ct),
                prefix: string.Empty,
                rawFiles,
                cancellationToken);

            string datasetRootPrefix = ResolveDatasetRootPrefix(rawFiles.Select(static file => file.RelativePath));
            Dictionary<string, ArchiveFileAccessor> indexedFiles = rawFiles
                .Select(file => new
                {
                    RelativePath = StripDatasetRootPrefix(file.RelativePath, datasetRootPrefix),
                    file.OpenReadAsync,
                })
                .Where(static file => !string.IsNullOrWhiteSpace(file.RelativePath))
                .DistinctBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    static file => file.RelativePath,
                    static file => new ArchiveFileAccessor(file.RelativePath, file.OpenReadAsync),
                    StringComparer.OrdinalIgnoreCase);

            return new ArchivePlateauDatasetContentSource(archivePath, indexedFiles);
        }

        public IReadOnlyList<string> EnumerateFiles()
        {
            return Files.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }

        public bool FileExists(string relativePath)
        {
            return Files.ContainsKey(NormalizeRelativePath(relativePath));
        }

        public async ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            string normalizedPath = NormalizeRelativePath(relativePath);
            if (!Files.TryGetValue(normalizedPath, out ArchiveFileAccessor? fileAccessor))
            {
                throw new FileNotFoundException($"The dataset entry '{relativePath}' was not found in '{ArchivePath}'.");
            }

            return await fileAccessor.OpenReadAsync(cancellationToken);
        }

        public async Task<string> MaterializeFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string normalizedPath = NormalizeSafeRelativePath(relativePath);
            string archiveCacheRoot = Path.Combine(
                outputRoot,
                ".dataset-cache",
                Path.GetFileNameWithoutExtension(ArchivePath));
            string destinationPath = ResolveMaterializedPath(archiveCacheRoot, normalizedPath);

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            if (File.Exists(destinationPath))
            {
                return destinationPath;
            }

            await using Stream sourceStream = await OpenReadAsync(normalizedPath, cancellationToken);
            await using FileStream destinationStream = new(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                useAsync: true);
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
            return destinationPath;
        }

        private static string ResolveMaterializedPath(string archiveCacheRoot, string normalizedRelativePath)
        {
            string normalizedRoot = Path.GetFullPath(archiveCacheRoot);
            string destinationPath = Path.GetFullPath(
                Path.Combine(
                    normalizedRoot,
                    normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));

            string relativePath = Path.GetRelativePath(
                normalizedRoot,
                destinationPath)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

            if (Path.IsPathRooted(relativePath)
                || string.Equals(relativePath, "..", StringComparison.Ordinal)
                || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"The dataset relative path '{normalizedRelativePath}' is outside the dataset cache directory.",
                    nameof(normalizedRelativePath));
            }

            return destinationPath;
        }

        private static async Task IndexArchiveAsync(
            string archivePath,
            Func<CancellationToken, ValueTask<Stream>> openArchiveStreamAsync,
            string prefix,
            ICollection<RawArchiveFileAccessor> results,
            CancellationToken cancellationToken)
        {
            await using Stream archiveStream = await openArchiveStreamAsync(cancellationToken);
            using IArchive archive = ArchiveFactory.OpenArchive(
                archiveStream,
                new ReaderOptions
                {
                    LeaveStreamOpen = false,
                });

            foreach (IArchiveEntry entry in archive.Entries.Where(static entry => !entry.IsDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string entryKey = NormalizeRelativePath(entry.Key ?? string.Empty);
                if (IsSupportedArchiveFile(entryKey))
                {
                    string nestedFileName = Path.GetFileNameWithoutExtension(entryKey);
                    string nestedParent = GetDirectoryPath(entryKey);
                    string nestedPrefix = PlateauPackageCatalog.TryNormalizePackageName(nestedFileName, out string? packageName)
                        ? CombineRelativePaths(prefix, nestedParent, "udx", packageName)
                        : CombineRelativePaths(prefix, nestedParent, nestedFileName);

                    await IndexArchiveAsync(
                        archivePath,
                        ct => OpenNestedArchiveStreamAsync(archivePath, openArchiveStreamAsync, entryKey, ct),
                        nestedPrefix,
                        results,
                        cancellationToken);
                    continue;
                }

                string relativePath = CombineRelativePaths(prefix, entryKey);
                results.Add(new RawArchiveFileAccessor(
                    relativePath,
                    ct => OpenEntryStreamAsync(archivePath, openArchiveStreamAsync, entryKey, ct)));
            }
        }

        private static async ValueTask<Stream> OpenArchiveFileStreamAsync(string archivePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new FileStream(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                useAsync: true);
        }

        private static async ValueTask<Stream> OpenNestedArchiveStreamAsync(
            string archivePath,
            Func<CancellationToken, ValueTask<Stream>> openArchiveStreamAsync,
            string entryKey,
            CancellationToken cancellationToken)
        {
            await using Stream nestedEntryStream = await OpenEntryStreamAsync(
                archivePath,
                openArchiveStreamAsync,
                entryKey,
                cancellationToken);
            MemoryStream buffer = new();
            await nestedEntryStream.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            return buffer;
        }

        private static async ValueTask<Stream> OpenEntryStreamAsync(
            string archivePath,
            Func<CancellationToken, ValueTask<Stream>> openArchiveStreamAsync,
            string entryKey,
            CancellationToken cancellationToken)
        {
            await using Stream archiveStream = await openArchiveStreamAsync(cancellationToken);
            using IArchive archive = ArchiveFactory.OpenArchive(
                archiveStream,
                new ReaderOptions
                {
                    LeaveStreamOpen = false,
                });

            IArchiveEntry entry = archive.Entries.FirstOrDefault(candidate =>
                    !candidate.IsDirectory
                    && string.Equals(
                        NormalizeRelativePath(candidate.Key ?? string.Empty),
                        NormalizeRelativePath(entryKey),
                        StringComparison.Ordinal))
                ?? throw new FileNotFoundException($"The dataset entry '{entryKey}' was not found in '{archivePath}'.");

            await using Stream entryStream = await entry.OpenEntryStreamAsync(cancellationToken);
            MemoryStream buffer = new();
            await entryStream.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            return buffer;
        }

        private static bool IsSupportedArchiveFile(string path)
        {
            string extension = Path.GetExtension(path);
            return string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveDatasetRootPrefix(IEnumerable<string> relativePaths)
        {
            string[] candidates = relativePaths
                .Select(NormalizeRelativePath)
                .Where(path => path.Contains("/udx/", StringComparison.Ordinal))
                .Select(path => path[..path.IndexOf("/udx/", StringComparison.Ordinal)])
                .Where(static path => path.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static path => path.Count(static character => character == '/'))
                .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return candidates.FirstOrDefault() ?? string.Empty;
        }

        private static string StripDatasetRootPrefix(string relativePath, string datasetRootPrefix)
        {
            string normalizedPath = NormalizeRelativePath(relativePath);
            if (string.IsNullOrEmpty(datasetRootPrefix))
            {
                return normalizedPath;
            }

            string normalizedPrefix = NormalizeRelativePath(datasetRootPrefix);
            return normalizedPath.StartsWith(normalizedPrefix + "/", StringComparison.OrdinalIgnoreCase)
                ? normalizedPath[(normalizedPrefix.Length + 1)..]
                : normalizedPath;
        }

    }
}
