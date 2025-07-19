using System;
using Microsoft.AspNetCore.Mvc;

namespace ufs.web;

public readonly record struct StreamResponse(FsPath Path, Stream Stream);
public static class StreamResponseExtensions
{
    public static IActionResult ActionResultAttachment(this StreamResponse self, HttpResponse http)
    {
        var fileName = self.Path.FileName.ToString();
        http.Headers.Append("Content-Disposition", $"attachment; filename=\"{fileName}\"");
        var ct = FileUtils.InferContentType(fileName);
        return new FileStreamResult(self.Stream, ct);
    }
    public static IActionResult ActionResultAttachment(this StreamResponse? self, HttpResponse http)
    {
        if (self is null)
            return new NotFoundResult();
        return self.Value.ActionResultAttachment(http);
    }
    public static IActionResult ActionResultInline(this StreamResponse self, HttpResponse http)
    {
        var fileName = self.Path.FileName.ToString();
        http.Headers.Append("Content-Disposition", $"inline; filename=\"{fileName}\"");
        var ct = FileUtils.InferContentType(fileName);
        return new FileStreamResult(self.Stream, ct);
    }
    public static IActionResult ActionResultInline(this StreamResponse? self, HttpResponse http)
    {
        if (self is null)
            return new NotFoundResult();
        return self.Value.ActionResultInline(http);
    }
}
