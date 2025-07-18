using System;

namespace ufs.web;

public interface IFileAuthorizer
{
    IAsyncEnumerable<FilePermissions> CheckPermissionsAsync(FsPath path, IFileSystem fs, HttpContext ctx, CancellationToken cancellationToken = default);
}
public abstract record FilePermissions
{
    private FilePermissions() { }

    public static readonly ReadPermission Read = new();
    public static readonly WritePermission Write = new();
    public static readonly DeletePermission Delete = new();
    public static MaxSizePermission MaxSize(long size) => new(size);
    public static readonly ListFilesShallowPermission ListFilesShallow = new();
    public static readonly ListFilesDeepPermission ListFilesDeep = new();
    public static readonly ListFilesAllPermission ListFilesAll = new();

    public record ReadPermission : FilePermissions;
    public record WritePermission : FilePermissions;
    public record DeletePermission : FilePermissions;
    public record MaxSizePermission(long Size) : FilePermissions;
    public record ListFilesShallowPermission : FilePermissions;
    public record ListFilesDeepPermission : FilePermissions;
    public record ListFilesAllPermission : FilePermissions;
}
public static class FilePermissionsExtensions
{
    public static async Task<IEnumerable<FilePermissions>> CheckPermissionsAsyncSafe(this IFileAuthorizer? authorizer, FsPath path, IFileSystem fs, HttpContext ctx, CancellationToken cancellationToken = default)
    {
        if (authorizer is null)
            return [];

        var perms = new List<FilePermissions>();
        await foreach (var perm in authorizer.CheckPermissionsAsync(path, fs, ctx, cancellationToken))
            perms.Add(perm);
        return perms;
    }
    public static bool CanRead(this IEnumerable<FilePermissions> perms) => perms.Any(p => p is FilePermissions.ReadPermission);
    public static bool CanWrite(this IEnumerable<FilePermissions> perms) => perms.Any(p => p is FilePermissions.WritePermission);
    public static bool CanDelete(this IEnumerable<FilePermissions> perms) => perms.Any(p => p is FilePermissions.DeletePermission);
    public static long? MaxSize(this IEnumerable<FilePermissions> perms)
    {
        long max = 0;
        foreach (var size in perms.OfType<FilePermissions.MaxSizePermission>().Select(p => p.Size))
            max = Math.Max(max, size);
        return max > 0 ? max : null;
    }
    public static bool CanListFiles(this IEnumerable<FilePermissions> perms, bool isDeep)
    {
        if (isDeep)
            return perms.Any(p => p is FilePermissions.ListFilesDeepPermission or FilePermissions.ListFilesAllPermission);
        return perms.Any(p => p is FilePermissions.ListFilesShallowPermission or FilePermissions.ListFilesAllPermission);
    }
}
