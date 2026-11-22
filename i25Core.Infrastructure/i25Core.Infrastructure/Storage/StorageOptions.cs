namespace i25Core.Infrastructure.Storage
{
    /// <summary>Bound from the "Storage" section of appsettings.json.</summary>
    public class StorageOptions
    {
        public const string SectionName = "Storage";

        /// <summary>The disk returned by IFileStorage / IFileStorageManager.Disk().</summary>
        public string Default { get; set; } = "local";

        /// <summary>
        /// Disks from configuration. Starts empty on purpose: configuration binding would otherwise MERGE
        /// your disks into built-in ones, so a disk you removed from appsettings.json would keep existing.
        /// Use <see cref="EffectiveDisks"/> to read the disks that are really in use.
        /// </summary>
        public Dictionary<string, DiskOptions> Disks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The configured disks, or the built-in "local" and "public" disks when none are configured.</summary>
        public IReadOnlyDictionary<string, DiskOptions> EffectiveDisks =>
            Disks.Count > 0
                ? Disks
                : new Dictionary<string, DiskOptions>(StringComparer.OrdinalIgnoreCase)
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

        public const string AnyExtension = "*";

        private static readonly string[] DefaultExtensions =
            { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf", ".txt", ".csv", ".docx", ".xlsx" };

        private string[]? _allowedExtensions;

        /// <summary>
        /// Accepted file extensions, with the dot, e.g. [".pdf", ".png"]. This list REPLACES the defaults.
        /// Use ["*"] to accept any extension (not recommended: a public disk could then serve uploaded
        /// .html or .svg files from your own domain). Not set = the defaults.
        /// Reads as empty until configured, so configuration binding replaces rather than appends.
        /// </summary>
        public string[] AllowedExtensions
        {
            get => _allowedExtensions ?? Array.Empty<string>();
            set => _allowedExtensions = value;
        }

        /// <summary>The extensions in force: the configured list, or the defaults.</summary>
        public IReadOnlyList<string> EffectiveAllowedExtensions => _allowedExtensions is { Length: > 0 } ? _allowedExtensions : DefaultExtensions;

        public bool AllowsAnyExtension => EffectiveAllowedExtensions.Contains(AnyExtension);

        /// <summary>Disk used by the upload helper when none is named.</summary>
        public string? Disk { get; set; }
    }
}
