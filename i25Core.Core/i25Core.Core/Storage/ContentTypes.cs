namespace i25Core.Core.Storage
{
    /// <summary>Content type from a file name's extension. Unknown extensions are "application/octet-stream".</summary>
    public static class ContentTypes
    {
        public const string Default = "application/octet-stream";

        private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = "text/plain",
            [".csv"] = "text/csv",
            [".html"] = "text/html",
            [".htm"] = "text/html",
            [".css"] = "text/css",
            [".js"] = "text/javascript",
            [".json"] = "application/json",
            [".xml"] = "application/xml",
            [".pdf"] = "application/pdf",
            [".zip"] = "application/zip",
            [".gz"] = "application/gzip",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".svg"] = "image/svg+xml",
            [".ico"] = "image/x-icon",
            [".mp3"] = "audio/mpeg",
            [".mp4"] = "video/mp4",
            [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".ppt"] = "application/vnd.ms-powerpoint",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation"
        };

        public static string FromFileName(string? fileName)
        {
            var extension = Path.GetExtension(fileName ?? string.Empty);
            return extension.Length > 0 && Map.TryGetValue(extension, out var type) ? type : Default;
        }
    }
}
