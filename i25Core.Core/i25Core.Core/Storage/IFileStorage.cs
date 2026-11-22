using i25Core.Core.Exceptions;

namespace i25Core.Core.Storage
{
    /// <summary>
    /// One storage disk: a place to keep files (Laravel's Storage::disk('x')).
    /// App code uses this and never touches the file system or a cloud SDK directly, so the
    /// disk can move from local folder to S3 or Azure through configuration.
    /// Paths are relative to the disk root, written with '/' (for example "invoices/2026/a.pdf").
    /// A path that escapes the disk ("../x", "C:\x") is rejected with ArgumentException (HTTP 400).
    /// </summary>
    public interface IFileStorage
    {
        /// <summary>The disk's configured name, e.g. "local" or "public".</summary>
        string Name { get; }

        /// <summary>
        /// Writes the stream to <paramref name="path"/>, creating folders as needed. The write is
        /// all-or-nothing: readers never see a half-written file.
        /// With overwrite = false an existing file throws <see cref="ConflictException"/> (HTTP 409).
        /// (Storage::put)
        /// </summary>
        Task<StoredFile> PutAsync(string path, Stream content, bool overwrite = true, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens the file for reading. The caller must dispose the stream.
        /// Throws <see cref="NotFoundException"/> (HTTP 404) when the file doesn't exist. (Storage::readStream)
        /// </summary>
        Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>(Storage::exists)</summary>
        Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>Size, modified time and content type, or null when the file doesn't exist. (Storage::size / lastModified)</summary>
        Task<StoredFile?> GetInfoAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>Deletes a file. Returns false when it didn't exist. (Storage::delete)</summary>
        Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>Deletes a folder and everything in it. Returns false when it didn't exist. The disk root itself can't be deleted. (Storage::deleteDirectory)</summary>
        Task<bool> DeleteDirectoryAsync(string directory, CancellationToken cancellationToken = default);

        /// <summary>Copies a file. Throws NotFoundException if the source is missing, ConflictException if the target exists and overwrite is false. (Storage::copy)</summary>
        Task<StoredFile> CopyAsync(string source, string target, bool overwrite = false, CancellationToken cancellationToken = default);

        /// <summary>Moves or renames a file. Same errors as CopyAsync. (Storage::move)</summary>
        Task<StoredFile> MoveAsync(string source, string target, bool overwrite = false, CancellationToken cancellationToken = default);

        /// <summary>Lists the files in a folder ("" = the disk root), sorted by path. Missing folder = empty list. (Storage::files / allFiles)</summary>
        Task<IReadOnlyList<StoredFile>> ListAsync(string directory = "", bool recursive = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// A public URL for the file, or null when the disk is private (no Url configured).
        /// The URL does not check that the file exists. (Storage::url)
        /// </summary>
        string? GetUrl(string path);
    }

    /// <summary>Picks a disk by name (Storage::disk('public')).</summary>
    public interface IFileStorageManager
    {
        /// <summary>The disk named by Storage:Default.</summary>
        IFileStorage Disk();

        /// <summary>A named disk from Storage:Disks. Unknown names throw InvalidOperationException listing the configured disks.</summary>
        IFileStorage Disk(string name);

        /// <summary>Names of the configured disks.</summary>
        IReadOnlyCollection<string> DiskNames { get; }
    }

    /// <summary>Convenience overloads built on the core members, so a new driver only implements IFileStorage.</summary>
    public static class FileStorageExtensions
    {
        public static Task<StoredFile> PutAsync(this IFileStorage disk, string path, byte[] content, bool overwrite = true, CancellationToken cancellationToken = default)
        {
            var stream = new MemoryStream(content, writable: false);
            return PutAndDisposeAsync(disk, path, stream, overwrite, cancellationToken);
        }

        public static Task<StoredFile> PutTextAsync(this IFileStorage disk, string path, string text, bool overwrite = true, CancellationToken cancellationToken = default)
        {
            var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text), writable: false);
            return PutAndDisposeAsync(disk, path, stream, overwrite, cancellationToken);
        }

        public static async Task<byte[]> GetBytesAsync(this IFileStorage disk, string path, CancellationToken cancellationToken = default)
        {
            await using var stream = await disk.OpenReadAsync(path, cancellationToken);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }

        public static async Task<string> GetTextAsync(this IFileStorage disk, string path, CancellationToken cancellationToken = default)
        {
            await using var stream = await disk.OpenReadAsync(path, cancellationToken);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            return await reader.ReadToEndAsync(cancellationToken);
        }

        private static async Task<StoredFile> PutAndDisposeAsync(IFileStorage disk, string path, MemoryStream stream, bool overwrite, CancellationToken cancellationToken)
        {
            await using (stream)
                return await disk.PutAsync(path, stream, overwrite, cancellationToken);
        }
    }
}
