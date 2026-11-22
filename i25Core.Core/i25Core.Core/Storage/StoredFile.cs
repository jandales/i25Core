namespace i25Core.Core.Storage
{
    /// <summary>Describes a file on a disk.</summary>
    public class StoredFile
    {
        /// <summary>Path relative to the disk root, with '/' separators.</summary>
        public string Path { get; set; } = string.Empty;

        public string Name => Path[(Path.LastIndexOf('/') + 1)..];

        public long Size { get; set; }

        public DateTime LastModifiedUtc { get; set; }

        /// <summary>Guessed from the file extension.</summary>
        public string ContentType { get; set; } = "application/octet-stream";

        /// <summary>Public URL when the disk has one; otherwise null.</summary>
        public string? Url { get; set; }
    }
}
