using System;
using System.Collections.Concurrent;

namespace ufs.Impl;

public class MountFileSystem : IFileSystem
{
    private readonly ConcurrentDictionary<FsPath, IFileSystem> mounts = [];

    public bool ReadOnly => true;

    public void Mount(FsPath path, IFileSystem fileSystem)
    {
        mounts[path] = fileSystem;
    }

    public void Unmount(FsPath path)
    {
        mounts.TryRemove(path, out _);
    }

    (FsPath relPath, IFileSystem fs) FsRelativePath(FsPath path)
    {
        foreach (var (entrypoint, fs) in mounts.OrderByDescending(m => m.Key.Value.Length))
        {
            if (path.InDirectory(entrypoint))
            {
                var relPath = path.ChangeDirectory(entrypoint, FsPath.Root);
                return (relPath, fs);
            }
        }
        throw new FileSystemException.NotFound(path.Value);
    }

    public IFileSystem At(FsPath path, FileSystemMode mode = FileSystemMode.Inherit)
    {
        var (rel, fs) = FsRelativePath(path);
        if (rel == FsPath.Root)
            return fs;
        return fs.At(rel, mode);
    }

    public Task<FileEntry.Directory> CreateDirectory(FsPath path, CancellationToken cancellationToken = default)
    {
        var (relPath, fs) = FsRelativePath(path);
        return fs.CreateDirectory(relPath, cancellationToken);
    }

    public Task<FileEntry.FileRW> CreateFile(FsPath path, CancellationToken cancellationToken = default)
    {
        var (relPath, fs) = FsRelativePath(path);
        return fs.CreateFile(relPath, cancellationToken);
    }

    public Task<bool> DeleteDirectory(FsPath path, bool recursive = false, CancellationToken cancellationToken = default)
    {
        var (relPath, fs) = FsRelativePath(path);
        return fs.DeleteDirectory(relPath, recursive, cancellationToken);
    }

    public Task<bool> DeleteFile(FsPath path, CancellationToken cancellationToken = default)
    {
        var (relPath, fs) = FsRelativePath(path);
        return fs.DeleteFile(relPath, cancellationToken);
    }

    public Task<bool> DirExists(FsPath path, CancellationToken cancellationToken = default)
    {
        var (relPath, fs) = FsRelativePath(path);
        return fs.DirExists(relPath, cancellationToken);
    }

    public IAsyncEnumerable<FileEntry> Entries(FsPath path, ListEntriesMode mode, CancellationToken cancellationToken = default)
    {
        var (relPath, fs) = FsRelativePath(path);
        return fs.Entries(relPath, mode, cancellationToken);
    }

    public Task<bool> FileExists(FsPath path, CancellationToken cancellationToken = default)
    {
        var (relPath, fs) = FsRelativePath(path);
        return fs.FileExists(relPath, cancellationToken);
    }

    public Task<FileStatus> FileStat(FsPath path, CancellationToken cancellationToken = default)
    {
        var (relPath, fs) = FsRelativePath(path);
        return fs.FileStat(relPath, cancellationToken);
    }

    public Task<FileEntry.FileRW> Integrate(FileEntry.IReadableFile file, CancellationToken cancellationToken = default)
    {
        // TODO: Maybe a bit problematic
        var (relPath, fs) = FsRelativePath(file.Path);
        return fs.Integrate(file, cancellationToken);
    }

    public Task<FileEntry.FileRO?> OpenFileRead(FsPath path, CancellationToken cancellationToken = default)
    {
        var (relPath, fs) = FsRelativePath(path);
        return fs.OpenFileRead(relPath, cancellationToken);
    }

    public Task<FileEntry.FileRW?> OpenFileReadWrite(FsPath path, CancellationToken cancellationToken = default)
    {
        var (relPath, fs) = FsRelativePath(path);
        return fs.OpenFileReadWrite(relPath, cancellationToken);
    }

    public Task<FileEntry.FileWO?> OpenFileWrite(FsPath path, CancellationToken cancellationToken = default)
    {
        var (relPath, fs) = FsRelativePath(path);
        return fs.OpenFileWrite(relPath, cancellationToken);
    }
}
