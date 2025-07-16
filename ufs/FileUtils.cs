using System;
using System.Text.RegularExpressions;

namespace ufs;

public static class FileUtils
{
    static readonly Dictionary<string, string> mimeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // text
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".csv"] = "text/csv",
        [".tsv"] = "text/tab-separated-values",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".yaml"] = "application/x-yaml",
        [".yml"] = "application/x-yaml",

        // web
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".css"] = "text/css",
        [".js"] = "application/javascript",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/vnd.microsoft.icon",

        // images
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".webp"] = "image/webp",

        // fonts
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
        [".eot"] = "application/vnd.ms-fontobject",

        // audio
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".ogg"] = "audio/ogg",
        [".aac"] = "audio/aac",
        [".flac"] = "audio/flac",

        // video
        [".mp4"] = "video/mp4",
        [".mov"] = "video/quicktime",
        [".webm"] = "video/webm",
        [".avi"] = "video/x-msvideo",
        [".mkv"] = "video/x-matroska",

        // docs
        [".pdf"] = "application/pdf",
        [".rtf"] = "application/rtf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",

        // archives
        [".zip"] = "application/zip",
        [".tar"] = "application/x-tar",
        [".gz"] = "application/gzip",
        [".7z"] = "application/x-7z-compressed",
        [".rar"] = "application/vnd.rar",

        // installers
        [".exe"] = "application/octet-stream",
        [".msi"] = "application/octet-stream",
        [".apk"] = "application/vnd.android.package-archive",
        [".deb"] = "application/vnd.debian.binary-package",
        [".rpm"] = "application/x-rpm",

        // misc
        [".epub"] = "application/epub+zip",
        [".mobi"] = "application/x-mobipocket-ebook",
        [".rss"] = "application/rss+xml",
        [".atom"] = "application/atom+xml",
        [".torrent"] = "application/x-bittorrent"
    };

    public static string InferContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (ext != null && mimeMap.TryGetValue(ext, out var contentType))
            return contentType;
        return "application/octet-stream";
    }

    public static string GlobFilterToPattern(string filter)
    {
        if (filter is "" or "*")
            return @".*";
        var escaped = Regex.Escape(filter)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".");
        return $"{escaped}$";
    }
}
