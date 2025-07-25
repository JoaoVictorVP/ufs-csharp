
namespace ufs.Impl;

public record class RealFileSystem(string Root, bool ReadOnly = false) : IFileSystem
{
    public static RealFileSystem AtTempDir(bool readOnly = false)
    {
        var tempPath = Path.GetTempPath();
        if (string.IsNullOrEmpty(tempPath))
            throw new InvalidOperationException("Could not determine temporary directory path.");
        return new RealFileSystem(tempPath, readOnly);
    }
    public static RealFileSystem AtAppDir<Program>()
    {
        var appDir = typeof(Program).Assembly.Location;
        var root = Path.GetDirectoryName(appDir)
            ?? throw new InvalidOperationException($"Could not determine application directory from '{appDir}'");
        return new RealFileSystem(root, false);
    }
    public static RealFileSystem AtWorkingDir(bool readOnly = false)
    {
        var workingDir = Directory.GetCurrentDirectory();
        if (string.IsNullOrEmpty(workingDir))
            throw new InvalidOperationException("Could not determine current working directory.");
        return new RealFileSystem(workingDir, readOnly);
    }

    public IFileSystem At(FsPath path, FileSystemMode mode = FileSystemMode.Inherit)
    {
        var resolvedPath = path.FullPath(Root)
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
        var resolvedPath = path.FullPath(Root)
            ?? throw new PathException.InvalidPath(path.Value);
        Directory.CreateDirectory(resolvedPath);
        return Task.FromResult(new FileEntry.Directory(path, this));
    }

    public async Task<FileEntry.FileRW> CreateFile(FsPath path, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(Root);
        var dir = path.DirectoryPath;
        await CreateDirectory(dir, cancellationToken);
        var resolvedPath = path.FullPath(Root)
            ?? throw new PathException.InvalidPath(path.Value);
        var file = File.Create(resolvedPath);
        return new FileEntry.FileRW(path, this, file);
    }

    public Task<bool> DeleteDirectory(FsPath path, bool recursive = false, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(Root);
        var resolvedPath = path.FullPath(Root)
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
        var resolvedPath = path.FullPath(Root)
            ?? throw new PathException.InvalidPath(path.Value);
        if (File.Exists(resolvedPath) is false)
            return Task.FromResult(false);
        
        File.Delete(resolvedPath);
        return Task.FromResult(true);
    }

    public Task<bool> DirExists(FsPath path, CancellationToken cancellationToken = default)
    {
        var resolvedPath = path.FullPath(Root)
            ?? throw new PathException.InvalidPath(path.Value);
        return Task.FromResult(Directory.Exists(resolvedPath));
    }

    public IAsyncEnumerable<FileEntry> Entries(FsPath path, ListEntriesMode mode, CancellationToken cancellationToken = default)
    {
        var resolvedPath = path.FullPath(Root)
            ?? throw new PathException.InvalidPath(path.Value);
        
        async IAsyncEnumerable<FileEntry> Empty()
        {
            await Task.Yield();
            yield break;
        }
        
        async IAsyncEnumerable<FileEntry> From(IEnumerable<string> entries)
        {
            await Task.Yield();
            foreach (var entry in entries)
            {
                int rootLen = Root switch
                {
                    [.., '/'] => Root.Length - 1,
                    _ => Root.Length
                };
                var entryPath = (entry.StartsWith(Root)
                    ? entry[rootLen..]
                    : entry).FsPath();
                if (Directory.Exists(entry))
                    yield return new FileEntry.Directory(entryPath, this);
                if (File.Exists(entry))
                    yield return new FileEntry.FileRef(entryPath, this);
            }
        }

        if (Directory.Exists(resolvedPath) is false)
            return Empty();            

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
        var resolvedPath = path.FullPath(Root)
            ?? throw new PathException.InvalidPath(path.Value);
        return Task.FromResult(File.Exists(resolvedPath));
    }

    public async Task<FileStatus> FileStat(FsPath path, CancellationToken cancellationToken = default)
    {
        if (await FileExists(path, cancellationToken) is false)
            return FileStatus.NotFound;
        return FileStatus.Exists;
    }

    public async Task<FileEntry.FileRW> Integrate(FileEntry.IReadableFile file, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(Root);
        var resolvedPath = file.Path.FullPath(Root)
            ?? throw new PathException.InvalidPath(file.Path.Value);
        var dir = file.Path.DirectoryPath;
        await CreateDirectory(dir, cancellationToken);

        var sourceStream = file.Inner;
        using var targetStream = File.Create(resolvedPath);
        await sourceStream.CopyToAsync(targetStream, cancellationToken);
        
        return new FileEntry.FileRW(file.Path, this, targetStream);
    }

    public Task<FileEntry.FileRO?> OpenFileRead(FsPath path, CancellationToken cancellationToken = default)
    {
        var resolvedPath = path.FullPath(Root)
            ?? throw new PathException.InvalidPath(path.Value);
        if (File.Exists(resolvedPath) is false)
            return Task.FromResult<FileEntry.FileRO?>(null);
        var file = File.OpenRead(resolvedPath);
        return Task.FromResult(new FileEntry.FileRO(path, this, file))!;
    }

    public async Task<FileEntry.FileRW?> OpenFileReadWrite(FsPath path, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(Root);
        var dir = path.DirectoryPath;
        await CreateDirectory(dir, cancellationToken);
        var resolvedPath = path.FullPath(Root)
            ?? throw new PathException.InvalidPath(path.Value);
        var file = File.Open(resolvedPath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        return new FileEntry.FileRW(path, this, file);
    }

    public Task<FileEntry.FileWO?> OpenFileWrite(FsPath path, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(Root);
        var resolvedPath = path.FullPath(Root)
            ?? throw new PathException.InvalidPath(path.Value);
        if (File.Exists(resolvedPath) is false)
            return Task.FromResult<FileEntry.FileWO?>(null);
        var file = File.Open(resolvedPath, FileMode.OpenOrCreate, FileAccess.Write);
        return Task.FromResult(new FileEntry.FileWO(path, this, file))!;
    }
}
