using Microsoft.AspNetCore.Mvc;

namespace ufs.web;

public static class WebAppExtensions
{
    public static WebApplication MapUfs(this WebApplication web, Action<RouteHandlerBuilder>? configureEp = null)
    {
        static FsPath UserPath(string path)
        {
            if (path.StartsWith('/') is false)
                path = '/' + path;
            return path.FsPath();
        }

        var gufs = web.MapGroup("/ufs").WithTags("ufs");
        configureEp?.Invoke(
            gufs.MapGet("/files/{*path}", async (string path, HttpContext context, [FromKeyedServices("ufs.web")] IFileSystem ufs, IFileAuthorizer? fileAuth) =>
            {
                var usePath = UserPath(path);
                var perms =
                    await fileAuth.CheckPermissionsAsyncSafe(usePath, ufs, context);
                if (perms.CanRead() is false)
                    return Results.Forbid();
                var file = await ufs.OpenFileRead(usePath);
                if (file is null)
                    return Results.NotFound($"File not found: {usePath}");

                var contentType = FileUtils.InferContentType(path);
                return Results.Stream(file.Inner.GetBackedStream(), contentType, usePath.FileName.ToString());
            })
        );
        configureEp?.Invoke(
            gufs.MapPut("/ufs/files/{*path}", async (string path, HttpContext context, [FromKeyedServices("ufs.web")] IFileSystem ufs, IFileAuthorizer? fileAuth) =>
            {
                var usePath = UserPath(path);
                var perms =
                    await fileAuth.CheckPermissionsAsyncSafe(usePath, ufs, context);
                if (perms.CanWrite() is false)
                    return Results.Forbid();
                var file = await ufs.OpenFileWrite(usePath);
                if (file is null)
                    return Results.Forbid();

                var innerStream = file.Inner;
                if (perms.MaxSize() is long maxSize)
                    innerStream = innerStream.WriteLimited(maxSize);
                using var stream = innerStream.GetBackedStream();
                await context.Request.Body.CopyToAsync(stream);
                await stream.FlushAsync();
                return Results.Ok();
            })
        );
        configureEp?.Invoke(
            gufs.MapDelete("/ufs/files/{*path}", async (string path, HttpContext context, [FromKeyedServices("ufs.web")] IFileSystem ufs, IFileAuthorizer? fileAuth) =>
            {
                var usePath = UserPath(path);
                var perms =
                    await fileAuth.CheckPermissionsAsyncSafe(usePath, ufs, context);
                if (perms.CanDelete() is false)
                    return Results.Forbid();
                var deleted = await ufs.DeleteFile(usePath);
                return Results.Ok();
            })
        );
        configureEp?.Invoke(
            gufs.MapMethods("/ufs/files/{*path}", [HttpMethods.Head], async (string path, HttpContext context, [FromKeyedServices("ufs.web")] IFileSystem ufs, IFileAuthorizer? fileAuth) =>
            {
                var usePath = UserPath(path);
                var perms =
                    await fileAuth.CheckPermissionsAsyncSafe(usePath, ufs, context);
                if (perms.CanRead() is false)
                    return Results.Forbid();
                var exists = await ufs.FileExists(usePath);
                if (exists is false)
                    return Results.NotFound($"File not found: {usePath}");
                return Results.Ok();
            })
        );

        configureEp?.Invoke(
            gufs.MapGet("/ufs/entries/{mode:regex(^(?:shallow|deep)$)}/{*path}", async (string path, string mode, HttpContext context, [FromKeyedServices("ufs.web")] IFileSystem ufs, IFileAuthorizer? fileAuth, CancellationToken ct, string filter = "") =>
            {
                var usePath = UserPath(path);
                var perms =
                    await fileAuth.CheckPermissionsAsyncSafe(usePath, ufs, context);
                bool isDeep = mode == "deep";
                if (perms.CanRead() is false || perms.CanListFiles(isDeep) is false)
                    return Results.Forbid();
                var listFilter = (isDeep, filter) switch
                {
                    (true, "") => ListEntriesMode.RecursiveAll,
                    (true, _) => ListEntriesMode.Recursive(filter),
                    (false, "") => ListEntriesMode.ShallowAll,
                    (false, _) => ListEntriesMode.Shallow(filter)
                };
                var entries = new List<string>();
                await foreach (var entry in ufs.Entries(usePath, listFilter, ct))
                {
                    if (entry is null)
                        continue;
                    entries.Add(entry.Path.Value);
                }
                return Results.Json(entries);
            })
        );

        return web;
    }
}
