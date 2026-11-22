namespace i25Core.Infrastructure.Storage
{
    /// <summary>Bound from the "Storage" section of appsettings.json.</summary>
    public class StorageOptions
    {
        public const string SectionName = "Storage";

        /// <summary>The disk returned by IFileStorage / IFileStorageManager.Disk().</summary>
        public string Default { get; set; } = "local";

        public Dictionary<string, DiskOptions> Disks { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["local"] = new DiskOptions { Driver = "Local", Root = "storage/app" },
            ["public"] = new DiskOptions { Driver = "Local", Root = "storage/public", Url = "/storage" }
        };

        public UploadOptions Uploads { get; set; } = new();

        /// <summary>The app's content root. Set by AddI25Storage; not read from configuration.</summary>
        public string RootPath { get; set; } = string.Empty;

        /// <summary>Resolves a relative disk root against the app's content root.</summary>
        public string Resolve(string path) =>
            Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(string.IsNullOrEmpty(RootPath) ? Directory.GetCurrentDirectory() : RootPath, path));
    }

    public class DiskOptions
    {
        /// <summary>"Local" is built in. Other values need a registered IStorageDriverFactory (S3, Azure...).</summary>
        public string Driver { get; set; } = "Local";

        /// <summary>Local driver: folder holding the files (relative to the app root, or absolute).</summary>
        public string Root { get; set; } = string.Empty;

        /// <summary>
        /// Makes the disk public. A path ("/storage") serves the folder from this app;
        /// a full address ("https://cdn.example.com/files") is used as is (the files are served elsewhere).
        /// Leave empty for a private disk.
        /// </summary>
        public string? Url { get; set; }

        /// <summary>Free-form settings for custom drivers, e.g. Bucket, Region.</summary>
        public Dictionary<string, string?> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Rules applied by the upload helper (IFileUploader).</summary>
    public class UploadOptions
    {
        /// <summary>Largest accepted upload in bytes. Default 10 MB.</summary>
        public long MaxBytes { get; set; } = 10 * 1024 * 1024;

        /// <summary>
        /// Accepted file extensions, with the dot. Empty list = accept any extension (not recommended:
        /// a public disk could then serve uploaded .html or .svg files from your own domain).
        /// </summary>
        public string[] AllowedExtensions { get; set; } =
            { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf", ".txt", ".csv", ".docx", ".xlsx" };

        /// <summary>Disk used by the upload helper when none is named.</summary>
        public string? Disk { get; set; }
    }
}
