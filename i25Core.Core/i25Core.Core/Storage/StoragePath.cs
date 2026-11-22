namespace i25Core.Core.Storage
{
    /// <summary>
    /// Validates and normalizes storage paths. Every driver must pass user-supplied paths through here,
    /// so "../../secret" and similar tricks are rejected the same way on every disk.
    /// Failures throw ArgumentException, which the API returns as HTTP 400.
    /// </summary>
    public static class StoragePath
    {
        /// <summary>Marks files that are still being written. Reserved: stored names may not end with it.</summary>
        public const string TempSuffix = ".i25tmp";

        private const int MaxSegmentLength = 255;
        private const int MaxPathLength = 1024;

        private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        private static readonly char[] InvalidChars =
            Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '|', '?', '*' }).Distinct().ToArray();

        /// <summary>A file path: at least one name. "a\b//c.txt" becomes "a/b/c.txt".</summary>
        public static string Normalize(string? path)
        {
            var normalized = Clean(path, allowEmpty: false);
            return normalized;
        }

        /// <summary>A folder path. An empty value means the disk root and returns "".</summary>
        public static string NormalizeDirectory(string? directory) => Clean(directory, allowEmpty: true);

        private static string Clean(string? path, bool allowEmpty)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                if (allowEmpty)
                    return string.Empty;

                throw Invalid(path, "the path is empty");
            }

            if (path.Length > MaxPathLength)
                throw Invalid(path[..40] + "...", $"the path is longer than {MaxPathLength} characters");

            // Accept both separators; empty parts ("a//b", leading "/") are dropped.
            var segments = path.Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0)
            {
                if (allowEmpty)
                    return string.Empty;

                throw Invalid(path, "the path has no file name");
            }

            foreach (var segment in segments)
                ValidateSegment(path, segment);

            return string.Join('/', segments);
        }

        private static void ValidateSegment(string original, string segment)
        {
            if (segment is "." or "..")
                throw Invalid(original, "'.' and '..' are not allowed");

            if (segment.Length > MaxSegmentLength)
                throw Invalid(original, $"a name is longer than {MaxSegmentLength} characters");

            if (segment.IndexOfAny(InvalidChars) >= 0 || segment.Any(char.IsControl))
                throw Invalid(original, "it contains characters that are not allowed in file names");

            if (segment.EndsWith(' ') || segment.EndsWith('.'))
                throw Invalid(original, "a name can't end with a space or a dot");

            var withoutExtension = segment.Contains('.') ? segment[..segment.IndexOf('.')] : segment;

            if (ReservedWindowsNames.Contains(withoutExtension))
                throw Invalid(original, $"'{withoutExtension}' is a reserved name");

            if (segment.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase))
                throw Invalid(original, $"names ending in '{TempSuffix}' are reserved");
        }

        private static ArgumentException Invalid(string? path, string reason) =>
            new($"'{path}' is not a valid storage path: {reason}.");
    }
}
