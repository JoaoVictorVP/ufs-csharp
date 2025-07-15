
namespace ufs.Impl;

public record class OverlayFileSystem(IFileSystem Lower, IFileSystem Upper)
    : IFileSystem
{
    public bool ReadOnly => Upper.ReadOnly;

    public IFileSystem At(FsPath path, FileSystemMode mode = FileSystemMode.Inherit)
    {
        var lowerDir = Lower.At(path, mode);
        var upperDir = Upper.At(path, mode);
        return new OverlayFileSystem(lowerDir, upperDir);
    }

    public Task<FileEntry.Directory> CreateDirectory(FsPath path, CancellationToken cancellationToken = default)
    {
        return Upper.CreateDirectory(path, cancellationToken);
    }

    public Task<FileEntry.FileRW> CreateFile(FsPath path, CancellationToken cancellationToken = default)
    {
        return Upper.CreateFile(path, cancellationToken);
    }

    public Task<bool> DeleteDirectory(FsPath path, bool recursive = false, CancellationToken cancellationToken = default)
    {
        return Upper.DeleteDirectory(path, recursive, cancellationToken);
    }

    public Task<bool> DeleteFile(FsPath path, CancellationToken cancellationToken = default)
    {
        return Upper.DeleteFile(path, cancellationToken);
    }

    public async Task<bool> DirExists(FsPath path, CancellationToken cancellationToken = default)
    {
        return await Upper.DirExists(path, cancellationToken)
            || await Lower.DirExists(path, cancellationToken);
    }

    public async IAsyncEnumerable<FileEntry> Entries(FsPath path, ListEntriesMode mode, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var listed = new HashSet<FsPath>();
        await foreach (var entry in Upper.Entries(path, mode, cancellationToken))
        {
            listed.Add(entry.Path);
            yield return entry;
        }

        await foreach (var entry in Lower.Entries(path, mode, cancellationToken))
        {
            if (listed.Contains(entry.Path))
                continue;
            if(await FileStat(entry.Path, cancellationToken) is FileStatus.Deleted)
                continue;
            yield return entry;
        }
    }

    public async Task<bool> FileExists(FsPath path, CancellationToken cancellationToken = default)
    {
        var stat = await FileStat(path, cancellationToken);
        return stat is FileStatus.Exists;
    }

    public async Task<FileStatus> FileStat(FsPath path, CancellationToken cancellationToken = default)
    {
        var overlayStatus = await Upper.FileStat(path, cancellationToken);
        if (overlayStatus is FileStatus.NotFound)
            return await Lower.FileStat(path, cancellationToken);
        return overlayStatus;
    }

    public Task<FileEntry.FileRW> Integrate(FileEntry.IReadableFile file, CancellationToken cancellationToken = default)
    {
        return Upper.Integrate(file, cancellationToken);
    }

    public async Task<FileEntry.FileRO?> OpenFileRead(FsPath path, CancellationToken cancellationToken = default)
    {
        return await Upper.OpenFileRead(path, cancellationToken)
            ?? await Lower.OpenFileRead(path, cancellationToken);
    }

    public async Task<FileEntry.FileRW?> OpenFileReadWrite(FsPath path, CancellationToken cancellationToken = default)
    {
        if (await Upper.FileExists(path, cancellationToken))
            return await Upper.OpenFileReadWrite(path, cancellationToken);
        var lowerFile = await Lower.OpenFileRead(path, cancellationToken);
        if (lowerFile is null)
            return await Upper.CreateFile(path, cancellationToken);
        return await Upper.Integrate(lowerFile, cancellationToken);
    }

    public async Task<FileEntry.FileWO?> OpenFileWrite(FsPath path, CancellationToken cancellationToken = default)
    {
        if (await Upper.FileExists(path, cancellationToken))
            return await Upper.OpenFileWrite(path, cancellationToken);
        var lowerFile = await Lower.OpenFileRead(path, cancellationToken);
        if (lowerFile is null)
            return (await Upper.CreateFile(path, cancellationToken)).WriteOnly();
        var upperRw = await Upper.Integrate(lowerFile, cancellationToken);
        return upperRw.WriteOnly();
    }
}
