using System;

namespace ufs.web;

public class DefaultWebFsProvider(IHttpContextAccessor http) : IWebFsProvider
{
    private readonly IHttpContextAccessor http = http;

    public ValueTask<string> GetDownloadUrl(FsPath path)
    {
        var baseUrl = http.HttpContext?.Request.Scheme + "://" + http.HttpContext?.Request.Host
            ?? throw new InvalidOperationException("HttpContext is not available to generate download URL.");
        return ValueTask.FromResult(
            $"{baseUrl}/ufs/files/{path.UriEncoded()}"
        );
    }
}
