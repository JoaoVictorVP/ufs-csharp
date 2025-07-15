
namespace ufs.Impl;

public record class OverlayFileSystem(IFileSystem Base, IFileSystem Overlay)
    : IFileSystem
{
    public bool ReadOnly => Overlay.ReadOnly;

    public IFileSystem At(FsPath path, FileSystemMode mode = FileSystemMode.Inherit)
    {
        var baseDir = Base.At(path, mode);
        var overlayDir = Overlay.At(path, mode);
        return new OverlayFileSystem(baseDir, overlayDir);
    }

    public Task<FileEntry.Directory> CreateDirectory(FsPath path, CancellationToken cancellationToken = default)
    {
        return Overlay.CreateDirectory(path, cancellationToken);
    }

    public Task<FileEntry.FileRW> CreateFile(FsPath path, CancellationToken cancellationToken = default)
    {
        return Overlay.CreateFile(path, cancellationToken);
    }

    public Task<bool> DeleteDirectory(FsPath path, bool recursive = false, CancellationToken cancellationToken = default)
    {
        return Overlay.DeleteDirectory(path, recursive, cancellationToken);
    }

    public Task<bool> DeleteFile(FsPath path, CancellationToken cancellationToken = default)
    {
        return Overlay.DeleteFile(path, cancellationToken);
    }

    public async Task<bool> DirExists(FsPath path, CancellationToken cancellationToken = default)
    {
        return await Overlay.DirExists(path, cancellationToken)
            || await Base.DirExists(path, cancellationToken);
    }

    public async IAsyncEnumerable<FileEntry> Entries(FsPath path, ListEntriesMode mode, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var listed = new HashSet<FsPath>();
        await foreach (var entry in Overlay.Entries(path, mode, cancellationToken))
        {
            listed.Add(entry.Path);
            yield return entry;
        }

        await foreach (var entry in Base.Entries(path, mode, cancellationToken))
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
        var overlayStatus = await Overlay.FileStat(path, cancellationToken);
        if (overlayStatus is FileStatus.NotFound)
            return await Base.FileStat(path, cancellationToken);
        return overlayStatus;
    }

    public Task<FileEntry.FileRW> Integrate(FileEntry.IReadableFile file, CancellationToken cancellationToken = default)
    {
        return Overlay.Integrate(file, cancellationToken);
    }

    public async Task<FileEntry.FileRO?> OpenFileRead(FsPath path, CancellationToken cancellationToken = default)
    {
        return await Overlay.OpenFileRead(path, cancellationToken)
            ?? await Base.OpenFileRead(path, cancellationToken);
    }

    public async Task<FileEntry.FileRW?> OpenFileReadWrite(FsPath path, CancellationToken cancellationToken = default)
    {
        if (await Overlay.FileExists(path, cancellationToken))
            return await Overlay.OpenFileReadWrite(path, cancellationToken);
        var baseFile = await Base.OpenFileRead(path, cancellationToken);
        if (baseFile is null)
            return null;
        return await Overlay.Integrate(baseFile, cancellationToken);
    }

    public async Task<FileEntry.FileWO?> OpenFileWrite(FsPath path, CancellationToken cancellationToken = default)
    {
        if (await Overlay.FileExists(path, cancellationToken))
            return await Overlay.OpenFileWrite(path, cancellationToken);
        var baseFile = await Base.OpenFileRead(path, cancellationToken);
        if (baseFile is null)
            return null;
        var overlayRw = await Overlay.Integrate(baseFile, cancellationToken);
        return overlayRw.WriteOnly();
    }
}
