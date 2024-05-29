namespace HybridWebView
{
    internal static class PathUtils
    {
        public static string NormalizePath(string filename) =>
            filename
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

        public static string MimeType (this string filePath)
        {
            var requestExtension = Path.GetExtension(filePath);
            var contentType = requestExtension switch
            {
                ".htm" or ".html" => "text/html",
                ".js" => "application/javascript",
                ".css" => "text/css",
                ".svg" => "image/svg+xml",
                ".png" => "image/png",
                ".jpg" => "image/jpg",
                ".gif" => "image/gif",
                _ => "text/plain",
            };

            return contentType;
        }
    }
}
