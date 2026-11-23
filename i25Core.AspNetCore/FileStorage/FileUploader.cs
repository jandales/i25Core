using i25Core.Core.Storage;
using i25Core.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace i25Core.AspNetCore.FileStorage
{
    /// <summary>What was stored for an upload.</summary>
    public class UploadResult
    {
        public string Disk { get; set; } = string.Empty;

        /// <summary>Path on the disk. Save this (for example in a database column) to find the file again.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>Public URL, or null when the disk is private.</summary>
        public string? Url { get; set; }

        public long Size { get; set; }
        public string ContentType { get; set; } = string.Empty;

        /// <summary>The name the user's file had, cleaned up for display. The stored name is a random one.</summary>
        public string OriginalName { get; set; } = string.Empty;
    }

    /// <summary>Validates and stores an uploaded file (Laravel: $request->file('x')->store(...)).</summary>
    public interface IFileUploader
    {
        /// <summary>
        /// Checks the rules in Storage:Uploads (size, extension), then stores the file under a random name
        /// ("{guid}.pdf") so a user can't choose the path, overwrite a file or smuggle in "../".
        /// A rule violation throws ArgumentException (HTTP 400).
        /// </summary>
        /// <param name="directory">Folder on the disk, e.g. "avatars". Empty = disk root.</param>
        /// <param name="disk">Disk name. Default: Storage:Uploads:Disk, then Storage:Default.</param>
        Task<UploadResult> StoreAsync(IFormFile file, string? directory = null, string? disk = null, CancellationToken cancellationToken = default);
    }

    public class FileUploader : IFileUploader
    {
        private readonly IFileStorageManager _storage;
        private readonly UploadOptions _rules;

        public FileUploader(IFileStorageManager storage, IOptions<StorageOptions> options)
        {
            _storage = storage;
            _rules = options.Value.Uploads;
        }

        public async Task<UploadResult> StoreAsync(IFormFile file, string? directory = null, string? disk = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(file);

            if (file.Length == 0)
                throw new ArgumentException("The uploaded file is empty.");

            if (_rules.MaxBytes > 0 && file.Length > _rules.MaxBytes)
                throw new ArgumentException($"The file is too large ({Megabytes(file.Length)} MB). The limit is {Megabytes(_rules.MaxBytes)} MB.");

            var originalName = CleanName(file.FileName);
            var extension = Path.GetExtension(originalName).ToLowerInvariant();

            if (!_rules.AllowsAnyExtension &&
                !_rules.EffectiveAllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                var shown = extension.Length == 0 ? "(none)" : extension;
                throw new ArgumentException(
                    $"Files of type '{shown}' are not allowed. Allowed: {string.Join(", ", _rules.EffectiveAllowedExtensions)}.");
            }

            var folder = StoragePath.NormalizeDirectory(directory);
            var storedName = Guid.NewGuid().ToString("N") + extension;
            var path = folder.Length == 0 ? storedName : $"{folder}/{storedName}";

            var target = !string.IsNullOrWhiteSpace(disk) ? _storage.Disk(disk)
                       : !string.IsNullOrWhiteSpace(_rules.Disk) ? _storage.Disk(_rules.Disk)
                       : _storage.Disk();

            await using var stream = file.OpenReadStream();
            var stored = await target.PutAsync(path, stream, overwrite: false, cancellationToken);

            return new UploadResult
            {
                Disk = target.Name,
                Path = stored.Path,
                Url = stored.Url,
                Size = stored.Size,
                // From the extension we checked, never from the browser's Content-Type header (which the user controls).
                ContentType = ContentTypes.FromFileName(stored.Path),
                OriginalName = originalName
            };
        }

        // "C:\fakepath\..\report.pdf" -> "report.pdf"; control characters removed; length capped.
        private static string CleanName(string? fileName)
        {
            var name = (fileName ?? string.Empty).Replace('\\', '/');
            name = name[(name.LastIndexOf('/') + 1)..];
            name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();

            return name.Length > 200 ? name[^200..] : name;
        }

        private static string Megabytes(long bytes) => (bytes / 1024d / 1024d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
