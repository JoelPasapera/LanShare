using System;
using System.Collections.Generic;
using System.IO;

namespace ServidorWebPro.Core
{
    /// <summary>
    /// Extension de archivo a Content-Type. Solo lectura tras la
    /// inicializacion estatica, asi que es seguro para lectores concurrentes.
    /// </summary>
    public static class MimeRegistry
    {
        public const string Fallback = "application/octet-stream";

        private static readonly Dictionary<string, string> _map =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".html", "text/html; charset=utf-8" },
            { ".htm",  "text/html; charset=utf-8" },
            { ".css",  "text/css; charset=utf-8" },
            { ".js",   "text/javascript; charset=utf-8" },
            { ".mjs",  "text/javascript; charset=utf-8" },
            { ".cjs",  "text/javascript; charset=utf-8" },
            { ".json", "application/json; charset=utf-8" },
            { ".map",  "application/json" },
            { ".png",  "image/png" },
            { ".jpg",  "image/jpeg" },
            { ".jpeg", "image/jpeg" },
            { ".gif",  "image/gif" },
            { ".svg",  "image/svg+xml" },
            { ".webp", "image/webp" },
            { ".avif", "image/avif" },
            { ".ico",  "image/x-icon" },
            { ".bmp",  "image/bmp" },
            { ".mp3",  "audio/mpeg" },
            { ".ogg",  "audio/ogg" },
            { ".oga",  "audio/ogg" },
            { ".wav",  "audio/wav" },
            { ".flac", "audio/flac" },
            { ".m4a",  "audio/mp4" },
            { ".mp4",  "video/mp4" },
            { ".m4v",  "video/mp4" },
            { ".webm", "video/webm" },
            { ".ogv",  "video/ogg" },
            { ".wasm", "application/wasm" },
            { ".pdf",  "application/pdf" },
            { ".txt",  "text/plain; charset=utf-8" },
            { ".md",   "text/markdown; charset=utf-8" },
            { ".csv",  "text/csv; charset=utf-8" },
            { ".xml",  "application/xml; charset=utf-8" },
            { ".webmanifest", "application/manifest+json" },
            { ".zip",  "application/zip" },
            { ".ttf",  "font/ttf" },
            { ".otf",  "font/otf" },
            { ".woff", "font/woff" },
            { ".woff2","font/woff2" },
            { ".eot",  "application/vnd.ms-fontobject" }
        };

        public static string Resolve(string filePath)
        {
            string ext = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(ext)) return Fallback;

            string mime;
            return _map.TryGetValue(ext, out mime) ? mime : Fallback;
        }

        public static bool IsHtml(string contentType)
        {
            return contentType != null &&
                   contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
        }
    }
}
