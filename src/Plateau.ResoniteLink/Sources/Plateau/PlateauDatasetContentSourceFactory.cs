using SharpCompress.Archives;
using SharpCompress.Readers;

namespace Plateau.ResoniteLink.Application.Importing;

public static class PlateauDatasetContentSourceFactory
{
    public static Task<IPlateauDatasetContentSource> CreateAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        return CreateAsync(
            sourcePath,
            new RemoteArchiveDistributionPolicy(),
            new ArchiveFileLayoutPolicy(),
            cancellationToken);
    }

    public static async Task<IPlateauDatasetContentSource> CreateAsync(
        string sourcePath,
        IRemoteArchiveDistributionPolicy remoteArchiveDistributionPolicy,
        IArchiveFileLayoutPolicy archiveFileLayoutPolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(remoteArchiveDistributionPolicy);
        ArgumentNullException.ThrowIfNull(archiveFileLayoutPolicy);

        string fullPath = Path.GetFullPath(sourcePath);
        if (Directory.Exists(fullPath))
        {
            return new LocalPlateauDatasetContentSource(
                PlateauDatasetPathResolver.ResolveDatasetRoot(fullPath),
                archiveFileLayoutPolicy);
        }

        if (!File.Exists(fullPath))
        {
            throw new PlateauImportValidationException([$"The local source path '{sourcePath}' does not exist."]);
        }

        bool isSupportedArchivePath = archiveFileLayoutPolicy.IsSupportedArchivePath(fullPath)
            || remoteArchiveDistributionPolicy.IsSupportedArchivePath(fullPath);
        if (!isSupportedArchivePath)
        {
            throw new PlateauImportValidationException(
                [$"The local source path '{sourcePath}' is not a supported dataset directory or archive."]);
        }

        return await ArchivePlateauDatasetContentSource.CreateAsync(fullPath, archiveFileLayoutPolicy, cancellationToken);
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

    internal static string? ResolveRelativePath(
        string baseRelativePath,
        string candidatePath,
        IArchiveFileLayoutPolicy archiveFileLayoutPolicy)
    {
        ArgumentNullException.ThrowIfNull(archiveFileLayoutPolicy);
        return archiveFileLayoutPolicy.ResolveRelativePath(baseRelativePath, candidatePath);
    }

    private sealed class LocalPlateauDatasetContentSource(
        string datasetRoot,
        IArchiveFileLayoutPolicy archiveFileLayoutPolicy) : IPlateauDatasetContentSource
    {
        public string SourcePath => datasetRoot;

        public IReadOnlyList<string> EnumerateFiles()
        {
            return Directory
                .EnumerateFiles(datasetRoot, "*", SearchOption.AllDirectories)
                .Select(path => archiveFileLayoutPolicy.NormalizeRelativePath(Path.GetRelativePath(datasetRoot, path)))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        public bool FileExists(string relativePath)
        {
            string absolutePath = Path.Combine(datasetRoot, archiveFileLayoutPolicy.NormalizeRelativePath(relativePath));
            return File.Exists(absolutePath);
        }

        public string? ResolveRelativePath(string baseRelativePath, string candidatePath)
        {
            return PlateauDatasetContentSourceFactory.ResolveRelativePath(
                baseRelativePath,
                candidatePath,
                archiveFileLayoutPolicy);
        }

        public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CA2000
            return ValueTask.FromResult<Stream>(OpenFileReadStream(datasetRoot, archiveFileLayoutPolicy, relativePath));
#pragma warning restore CA2000
        }

        public Task<string> MaterializeFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Path.Combine(datasetRoot, archiveFileLayoutPolicy.NormalizeRelativePath(relativePath)));
        }

        private static FileStream OpenFileReadStream(
            string datasetRoot,
            IArchiveFileLayoutPolicy archiveFileLayoutPolicy,
            string relativePath)
        {
            return new FileStream(
                Path.Combine(datasetRoot, archiveFileLayoutPolicy.NormalizeRelativePath(relativePath)),
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
        private IArchiveFileLayoutPolicy ArchiveFileLayoutPolicy { get; }

        private ArchivePlateauDatasetContentSource(
            string archivePath,
            IReadOnlyDictionary<string, ArchiveFileAccessor> files,
            IArchiveFileLayoutPolicy archiveFileLayoutPolicy)
        {
            ArchivePath = archivePath;
            Files = files;
            ArchiveFileLayoutPolicy = archiveFileLayoutPolicy;
        }

        public string SourcePath => ArchivePath;

        public static async Task<ArchivePlateauDatasetContentSource> CreateAsync(
            string archivePath,
            IArchiveFileLayoutPolicy archiveFileLayoutPolicy,
            CancellationToken cancellationToken)
        {
            List<RawArchiveFileAccessor> rawFiles = [];
            await IndexArchiveAsync(
                archivePath,
                ct => OpenArchiveFileStreamAsync(archivePath, ct),
                prefix: string.Empty,
                rawFiles,
                archiveFileLayoutPolicy,
                cancellationToken);

            string datasetRootPrefix = archiveFileLayoutPolicy.ResolveDatasetRootPrefix(rawFiles.Select(static file => file.RelativePath));
            Dictionary<string, ArchiveFileAccessor> indexedFiles = rawFiles
                .Select(file => new
                {
                    RelativePath = archiveFileLayoutPolicy.StripDatasetRootPrefix(file.RelativePath, datasetRootPrefix),
                    file.OpenReadAsync,
                })
                .Where(static file => !string.IsNullOrWhiteSpace(file.RelativePath))
                .DistinctBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    static file => file.RelativePath,
                    static file => new ArchiveFileAccessor(file.RelativePath, file.OpenReadAsync),
                    StringComparer.OrdinalIgnoreCase);

            return new ArchivePlateauDatasetContentSource(archivePath, indexedFiles, archiveFileLayoutPolicy);
        }

        public IReadOnlyList<string> EnumerateFiles()
        {
            return Files.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }

        public bool FileExists(string relativePath)
        {
            return Files.ContainsKey(ArchiveFileLayoutPolicy.NormalizeRelativePath(relativePath));
        }

        public string? ResolveRelativePath(string baseRelativePath, string candidatePath)
        {
            return PlateauDatasetContentSourceFactory.ResolveRelativePath(
                baseRelativePath,
                candidatePath,
                ArchiveFileLayoutPolicy);
        }

        public async ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            string normalizedPath = ArchiveFileLayoutPolicy.NormalizeRelativePath(relativePath);
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
            string archiveCacheRoot = ArchiveFileLayoutPolicy.GetMaterializedArchiveRoot(outputRoot, ArchivePath);
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
            IArchiveFileLayoutPolicy archiveFileLayoutPolicy,
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

                string entryKey = archiveFileLayoutPolicy.NormalizeRelativePath(entry.Key ?? string.Empty);
                if (IsSupportedArchiveFile(entryKey))
                {
                    await IndexArchiveAsync(
                        archivePath,
                        ct => OpenNestedArchiveStreamAsync(archivePath, openArchiveStreamAsync, entryKey, archiveFileLayoutPolicy, ct),
                        archiveFileLayoutPolicy.GetNestedArchivePrefix(prefix, entryKey),
                        results,
                        archiveFileLayoutPolicy,
                        cancellationToken);
                    continue;
                }

                string relativePath = archiveFileLayoutPolicy.CombineRelativePaths(prefix, entryKey);
                results.Add(new RawArchiveFileAccessor(
                    relativePath,
                    ct => OpenEntryStreamAsync(archivePath, openArchiveStreamAsync, entryKey, archiveFileLayoutPolicy, ct)));
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
            IArchiveFileLayoutPolicy archiveFileLayoutPolicy,
            CancellationToken cancellationToken)
        {
            await using Stream nestedEntryStream = await OpenEntryStreamAsync(
                archivePath,
                openArchiveStreamAsync,
                entryKey,
                archiveFileLayoutPolicy,
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
            IArchiveFileLayoutPolicy archiveFileLayoutPolicy,
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
                        archiveFileLayoutPolicy.NormalizeRelativePath(candidate.Key ?? string.Empty),
                        archiveFileLayoutPolicy.NormalizeRelativePath(entryKey),
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

    }
}
