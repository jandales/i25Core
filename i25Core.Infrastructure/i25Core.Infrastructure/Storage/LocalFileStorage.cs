using i25Core.Core.Exceptions;
using i25Core.Core.Storage;

namespace i25Core.Infrastructure.Storage
{
    /// <summary>
    /// A disk backed by a folder on the server's file system.
    /// Thread-safe: it holds no state besides its settings.
    /// </summary>
    public class LocalFileStorage : IFileStorage
    {
        private static readonly StringComparison PathComparison =
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private readonly string _root;              // full path, always ending with a separator
        private readonly string? _url;

        public LocalFileStorage(string name, string rootFolder, string? url)
        {
            Name = name;
            _url = string.IsNullOrWhiteSpace(url) ? null : url.Trim().TrimEnd('/');

            var full = Path.GetFullPath(rootFolder);
            Directory.CreateDirectory(full);
            _root = Path.TrimEndingDirectorySeparator(full) + Path.DirectorySeparatorChar;
        }

        public string Name { get; }

        /// <summary>The disk's folder on the server, without a trailing separator.</summary>
        public string RootFolder => Path.TrimEndingDirectorySeparator(_root);

        public string? PublicUrlBase => _url;

        public async Task<StoredFile> PutAsync(string path, Stream content, bool overwrite = true, CancellationToken cancellationToken = default)
        {
            var relative = StoragePath.Normalize(path);
            var full = Resolve(relative);

            if (!overwrite && File.Exists(full))
                throw new ConflictException($"File '{relative}' already exists on disk '{Name}'.");

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);

            // Write beside the target, then move it into place: readers see the old file or the new one, never half of one.
            var temp = Path.Combine(Path.GetDirectoryName(full)!, $".{Guid.NewGuid():N}{StoragePath.TempSuffix}");

            try
            {
                await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await content.CopyToAsync(file, cancellationToken);
                    await file.FlushAsync(cancellationToken);
                }

                await MoveIntoPlaceAsync(temp, full, relative, overwrite, cancellationToken);
            }
            finally
            {
                if (File.Exists(temp))
                    TryDelete(temp);
            }

            return Describe(relative, new FileInfo(full));
        }

        /// <summary>
        /// The final step of a write: replace the target with the finished temp file.
        /// On Windows this can fail for a moment while another request is replacing, reading or
        /// scanning the same file (antivirus, indexer), so it is retried a few times with a short pause.
        /// </summary>
        private async Task MoveIntoPlaceAsync(string temp, string full, string relative, bool overwrite, CancellationToken cancellationToken)
        {
            const int maxAttempts = 14;         // about 1.5 seconds in total before giving up

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(temp, full, overwrite);
                    return;
                }
                catch (IOException) when (!overwrite && File.Exists(full))
                {
                    // Someone created it between our check and the move: a real conflict, not a retry case.
                    throw new ConflictException($"File '{relative}' already exists on disk '{Name}'.");
                }
                catch (Exception ex) when (attempt < maxAttempts && ex is UnauthorizedAccessException or IOException)
                {
                    await Task.Delay(Math.Min(20 * attempt, 120), cancellationToken);
                }
            }
        }

        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
        {
            var relative = StoragePath.Normalize(path);
            var full = Resolve(relative);

            if (!File.Exists(full))
                throw NotFound(relative);

            Stream stream;

            try
            {
                // FileShare.Delete: on Windows an open reader would otherwise block an overwrite or delete
                // of the same file. With it, the writer wins and the reader finishes reading the old content.
                stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            catch (FileNotFoundException)
            {
                throw NotFound(relative);       // deleted between the check and the open
            }
            catch (DirectoryNotFoundException)
            {
                throw NotFound(relative);
            }

            return Task.FromResult(stream);
        }

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(File.Exists(Resolve(StoragePath.Normalize(path))));

        public Task<StoredFile?> GetInfoAsync(string path, CancellationToken cancellationToken = default)
        {
            var relative = StoragePath.Normalize(path);
            var info = new FileInfo(Resolve(relative));

            return Task.FromResult<StoredFile?>(info.Exists ? Describe(relative, info) : null);
        }

        public Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            var full = Resolve(StoragePath.Normalize(path));

            if (!File.Exists(full))
                return Task.FromResult(false);

            File.Delete(full);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteDirectoryAsync(string directory, CancellationToken cancellationToken = default)
        {
            var relative = StoragePath.NormalizeDirectory(directory);

            if (relative.Length == 0)
                throw new ArgumentException("The root of a disk can't be deleted. Name a folder inside it.");

            var full = Resolve(relative);

            if (!Directory.Exists(full))
                return Task.FromResult(false);

            Directory.Delete(full, recursive: true);
            return Task.FromResult(true);
        }

        public async Task<StoredFile> CopyAsync(string source, string target, bool overwrite = false, CancellationToken cancellationToken = default)
        {
            var from = StoragePath.Normalize(source);
            var to = StoragePath.Normalize(target);

            // Reuses PutAsync, so the copy is atomic too.
            await using var stream = await OpenReadAsync(from, cancellationToken);
            return await PutAsync(to, stream, overwrite, cancellationToken);
        }

        public async Task<StoredFile> MoveAsync(string source, string target, bool overwrite = false, CancellationToken cancellationToken = default)
        {
            var from = StoragePath.Normalize(source);
            var to = StoragePath.Normalize(target);
            var fullFrom = Resolve(from);
            var fullTo = Resolve(to);

            if (!File.Exists(fullFrom))
                throw NotFound(from);

            if (!overwrite && File.Exists(fullTo))
                throw new ConflictException($"File '{to}' already exists on disk '{Name}'.");

            Directory.CreateDirectory(Path.GetDirectoryName(fullTo)!);

            await MoveIntoPlaceAsync(fullFrom, fullTo, to, overwrite, cancellationToken);

            return Describe(to, new FileInfo(fullTo));
        }

        public Task<IReadOnlyList<StoredFile>> ListAsync(string directory = "", bool recursive = false, CancellationToken cancellationToken = default)
        {
            var relative = StoragePath.NormalizeDirectory(directory);
            var full = relative.Length == 0 ? RootFolder : Resolve(relative);

            if (!Directory.Exists(full))
                return Task.FromResult<IReadOnlyList<StoredFile>>(Array.Empty<StoredFile>());

            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            var files = new DirectoryInfo(full)
                .EnumerateFiles("*", option)
                .Where(f => !f.Name.EndsWith(StoragePath.TempSuffix, StringComparison.OrdinalIgnoreCase))   // files still being written
                .Select(f => Describe(ToRelative(f.FullName), f))
                .OrderBy(f => f.Path, StringComparer.Ordinal)
                .ToList();

            return Task.FromResult<IReadOnlyList<StoredFile>>(files);
        }

        public string? GetUrl(string path)
        {
            if (_url == null)
                return null;

            var relative = StoragePath.Normalize(path);
            var encoded = string.Join('/', relative.Split('/').Select(Uri.EscapeDataString));

            return $"{_url}/{encoded}";
        }

        // ---- helpers ----

        /// <summary>Combines a normalized path with the root and makes sure the result is still inside it.</summary>
        private string Resolve(string relative)
        {
            var full = Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));

            // StoragePath already rejects "..", so this is a second, independent guard.
            if (!full.StartsWith(_root, PathComparison))
                throw new ArgumentException($"'{relative}' points outside the storage disk.");

            return full;
        }

        private string ToRelative(string fullPath) =>
            fullPath[_root.Length..].Replace(Path.DirectorySeparatorChar, '/');

        private StoredFile Describe(string relative, FileInfo info) => new()
        {
            Path = relative,
            Size = info.Length,
            LastModifiedUtc = info.LastWriteTimeUtc,
            ContentType = ContentTypes.FromFileName(relative),
            Url = GetUrl(relative)
        };

        private NotFoundException NotFound(string relative) =>
            new($"File '{relative}' was not found on disk '{Name}'.");

        private static void TryDelete(string file)
        {
            try { File.Delete(file); } catch (IOException) { /* a leftover temp file is harmless and ignored by listings */ }
        }
    }
}
