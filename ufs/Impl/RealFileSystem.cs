
namespace ufs.Impl;

public record class RealFileSystem(string Root, bool ReadOnly = false) : IFileSystem
{
    public static RealFileSystem AtAppDir<Self>()
    {
        var appDir = typeof(Self).Assembly.Location;
        var root = Path.GetDirectoryName(appDir)
            ?? throw new InvalidOperationException($"Could not determine application directory from '{appDir}'");
        return new RealFileSystem(root, false);
    }

    public IFileSystem At(FsPath path, FileSystemMode mode = FileSystemMode.Inherit)
    {
        var resolvedPath = path.Resolve(Root, Root)
            ?? throw new PathException.InvalidPath(path.Value);

        var nextReadOnly = mode switch
        {
            FileSystemMode.Inherit => ReadOnly,
            FileSystemMode.ReadOnly => true,
            FileSystemMode.ReadWrite => false,
            _ => throw new NotSupportedException($"FileSystemMode '{mode}' is not supported.")
        };
        if (nextReadOnly is false && ReadOnly)
            throw new FileSystemException.ReadOnly(Root);

        return new RealFileSystem(resolvedPath, nextReadOnly);
    }

    public Task<FileEntry.Directory> CreateDirectory(FsPath path, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(Root);
        var resolvedPath = path.Resolve(Root, Root)
            ?? throw new PathException.InvalidPath(path.Value);
        Directory.CreateDirectory(resolvedPath);
        return Task.FromResult(new FileEntry.Directory(resolvedPath.FsPath(), this));
    }

    public Task<FileEntry.FileRW> CreateFile(FsPath path, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(Root);
        var resolvedPath = path.Resolve(Root, Root)
            ?? throw new PathException.InvalidPath(path.Value);
        var file = File.Create(resolvedPath);
        return Task.FromResult(new FileEntry.FileRW(resolvedPath.FsPath(), this, file));
    }

    public Task<bool> DeleteDirectory(FsPath path, bool recursive = false, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(Root);
        var resolvedPath = path.Resolve(Root, Root)
            ?? throw new PathException.InvalidPath(path.Value);
        if (Directory.Exists(resolvedPath) is false)
            return Task.FromResult(false);
        
        if (recursive)
            Directory.Delete(resolvedPath, true);
        else
            Directory.Delete(resolvedPath);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteFile(FsPath path, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(Root);
        var resolvedPath = path.Resolve(Root, Root)
            ?? throw new PathException.InvalidPath(path.Value);
        if (File.Exists(resolvedPath) is false)
            return Task.FromResult(false);
        
        File.Delete(resolvedPath);
        return Task.FromResult(true);
    }

    public Task<bool> DirExists(FsPath path, CancellationToken cancellationToken = default)
    {
        var resolvedPath = path.Resolve(Root, Root)
            ?? throw new PathException.InvalidPath(path.Value);
        return Task.FromResult(Directory.Exists(resolvedPath));
    }

    public IAsyncEnumerable<FileEntry> Entries(FsPath path, ListEntriesMode mode, CancellationToken cancellationToken = default)
    {
        var resolvedPath = path.Resolve(Root, Root)
            ?? throw new PathException.InvalidPath(path.Value);
        
        async IAsyncEnumerable<FileEntry> From(IEnumerable<string> entries)
        {
            await Task.Yield();
            foreach (var entry in entries)
            {
                if (Directory.Exists(entry))
                    yield return new FileEntry.Directory(entry.FsPath(), this);
                if (File.Exists(entry))
                    yield return new FileEntry.FileRef(entry.FsPath(), this);
            }
        }

        return mode switch
        {
            ListEntriesMode.ShallowMode shallow =>
                shallow.Filter is "*"
                    ? From(Directory.EnumerateFileSystemEntries(resolvedPath))
                    : From(Directory.EnumerateFileSystemEntries(resolvedPath, shallow.Filter)),
            ListEntriesMode.RecursiveMode recursive =>
                From(Directory.EnumerateFileSystemEntries(resolvedPath, recursive.Filter, SearchOption.AllDirectories)),
            _ => throw new NotSupportedException($"ListEntriesMode '{mode.GetType().Name}'")
        };
    }

    public Task<bool> FileExists(FsPath path, CancellationToken cancellationToken = default)
    {
        var resolvedPath = path.Resolve(Root, Root)
            ?? throw new PathException.InvalidPath(path.Value);
        return Task.FromResult(File.Exists(resolvedPath));
    }

    public Task<FileEntry.FileRO?> OpenFileRead(FsPath path, CancellationToken cancellationToken = default)
    {
        var resolvedPath = path.Resolve(Root, Root)
            ?? throw new PathException.InvalidPath(path.Value);
        if (File.Exists(resolvedPath) is false)
            return Task.FromResult<FileEntry.FileRO?>(null);
        var file = File.OpenRead(resolvedPath);
        return Task.FromResult(new FileEntry.FileRO(resolvedPath.FsPath(), this, file))!;
    }

    public Task<FileEntry.FileRW?> OpenFileReadWrite(FsPath path, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(Root);
        var resolvedPath = path.Resolve(Root, Root)
            ?? throw new PathException.InvalidPath(path.Value);
        var file = File.Open(resolvedPath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        return Task.FromResult(new FileEntry.FileRW(resolvedPath.FsPath(), this, file))!;
    }

    public Task<FileEntry.FileWO?> OpenFileWrite(FsPath path, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(Root);
        var resolvedPath = path.Resolve(Root, Root)
            ?? throw new PathException.InvalidPath(path.Value);
        if (File.Exists(resolvedPath) is false)
            return Task.FromResult<FileEntry.FileWO?>(null);
        var file = File.Open(resolvedPath, FileMode.OpenOrCreate, FileAccess.Write);
        return Task.FromResult(new FileEntry.FileWO(resolvedPath.FsPath(), this, file))!;
    }
}
