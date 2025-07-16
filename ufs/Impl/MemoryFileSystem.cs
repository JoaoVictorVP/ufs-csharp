
using System.Text.RegularExpressions;
using ufs.Impl.InMemory;

namespace ufs.Impl;

public record class MemoryFileSystem(MemoryFileTree.Directory Root) : IFileSystem
{
    private MemoryFileTree.Directory Root { get; init; } = Root;
    public bool ReadOnly => Root.ReadOnly;

    public IFileSystem At(FsPath path, FileSystemMode mode = FileSystemMode.Inherit)
    {
        var nextReadOnly = mode switch
        {
            FileSystemMode.Inherit => ReadOnly,
            FileSystemMode.ReadOnly => true,
            FileSystemMode.ReadWrite => false,
            _ => throw new NotSupportedException($"FileSystemMode '{mode}' is not supported.")
        };
        if (nextReadOnly is false && ReadOnly)
            throw new FileSystemException.ReadOnly(path.Value);
        var (treeDir, _) = CreateDirectorySync(path, nextReadOnly);
        return new MemoryFileSystem(treeDir);
    }

    (MemoryFileTree.Directory treeDir, FileEntry.Directory dir) CreateDirectorySync(FsPath path, bool readOnly = false)
    {
        var (lastExistingDir, needToCreateDirs) = Root.RecursiveWalk(path)
            .Where(pair => pair.dir is not null)
            .LastOrDefault();
        if (lastExistingDir is null)
            throw new FileSystemException.NotFound(path.Value);
        if (lastExistingDir.ReadOnly)
            throw new FileSystemException.ReadOnly(path.Value);
        if(needToCreateDirs.IsEmpty)
            return (lastExistingDir, new FileEntry.Directory(path, this));
        var currentDirName = needToCreateDirs.Span[0];
        needToCreateDirs = needToCreateDirs[1..];
        var directory =
            lastExistingDir.Path().Value == path.FullPath(Root.Path().Value)
                ? lastExistingDir
                : lastExistingDir.CreateDir(currentDirName.Span.ToString(), readOnly);
        while(needToCreateDirs.IsEmpty is false)
        {
            currentDirName = needToCreateDirs.Span[0];
            needToCreateDirs = needToCreateDirs[1..];
            directory = directory.CreateDir(currentDirName.Span.ToString(), readOnly);
        }
        return (directory, new FileEntry.Directory(path, this));
    }
    public Task<FileEntry.Directory> CreateDirectory(FsPath path, CancellationToken cancellationToken = default)
    {
        var (_, dir) = CreateDirectorySync(path, false);
        return Task.FromResult(dir);
    }

    public Task<FileEntry.FileRW> CreateFile(FsPath path, CancellationToken cancellationToken = default)
    {
        var (dir, _) = CreateDirectorySync(path.DirectoryPath);
        if (dir is null)
            throw new FileSystemException.NotFound(path.DirectoryPath.Value);
        if (dir.ReadOnly)
                throw new FileSystemException.ReadOnly(path.Value);
        var file = dir.CreateFile(path.FileName.ToString(), new MemoryStream(8192));
        return Task.FromResult(new FileEntry.FileRW(path, this, file.Stream.Mirror()));
    }

    public Task<bool> DeleteDirectory(FsPath path, bool recursive = false, CancellationToken cancellationToken = default)
    {
        var baseDir = Root.FindDirectory(path.DirectoryPath)
            ?? throw new FileSystemException.NotFound(path.Value);
        var treeDir = baseDir.GetDirectory(path.FileName.ToString());
        if(treeDir is null)
            return Task.FromResult(false);
        if (baseDir.ReadOnly)
                throw new FileSystemException.ReadOnly(path.Value);
        baseDir.DeleteDir(treeDir);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteFile(FsPath path, CancellationToken cancellationToken = default)
    {
        var dir = Root.FindDirectory(path.DirectoryPath);
        if (dir is null)
            throw new FileSystemException.NotFound(path.Value);
        if (dir.ReadOnly)
            throw new FileSystemException.ReadOnly(path.Value);
        var file = dir.GetFile(path.FileName.ToString());
        if (file is null)
        {
            Root.TombstoneFile(path);
            return Task.FromResult(false);
        }
        Root.DeleteFile(file);
        return Task.FromResult(true);
    }

    public Task<bool> DirExists(FsPath path, CancellationToken cancellationToken = default)
    {
        var dir = Root.FindDirectory(path);
        return Task.FromResult(dir is not null);
    }

    public async IAsyncEnumerable<FileEntry> Entries(FsPath path, ListEntriesMode mode, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var localRoot = Root.FindDirectory(path)
            ?? throw new FileSystemException.NotFound(path.Value);

        static string ToPattern(string filter)
            => FileUtils.GlobFilterToPattern(filter);

        var rootPath = Root.Path();
        FsPath RelativePath(FsPath fullPath)
        {
            int rootLen = rootPath.Value switch
            {
                [.., '/'] => rootPath.Value.Length - 1,
                _ => rootPath.Value.Length
            };
            var entryPath = (fullPath.Value.StartsWith(rootPath.Value)
                ? fullPath.Value[rootLen..]
                : fullPath.Value).FsPath();
            return entryPath;
        }

        switch (mode)
        {
            case ListEntriesMode.ShallowMode shallow:
                foreach (var dir in localRoot.Directories.Values)
                {
                    if (shallow.IsAll || Regex.IsMatch(dir.Name.ToString(), ToPattern(shallow.Filter), RegexOptions.IgnoreCase))
                        yield return new FileEntry.Directory(RelativePath(dir.Path()), this);
                }
                foreach (var file in localRoot.Files.Values)
                {
                    if (shallow.IsAll || Regex.IsMatch(file.Name.ToString(), ToPattern(shallow.Filter), RegexOptions.IgnoreCase))
                        yield return new FileEntry.FileRef(RelativePath(file.Path()), this);
                }
                break;
            case ListEntriesMode.RecursiveMode recursive:
                foreach (var entry in localRoot.RecursiveEntries())
                {
                    switch (entry)
                    {
                        case MemoryFileTree.Directory dir when recursive.IsAll || Regex.IsMatch(dir.Name.ToString(), ToPattern(recursive.Filter), RegexOptions.IgnoreCase):
                            yield return new FileEntry.Directory(RelativePath(dir.Path()), this);
                            break;
                        case MemoryFileTree.File file when recursive.IsAll || Regex.IsMatch(file.Name.ToString(), ToPattern(recursive.Filter), RegexOptions.IgnoreCase):
                            yield return new FileEntry.FileRef(RelativePath(file.Path()), this);
                            break;
                    }
                }
                break;
            default:
                throw new NotSupportedException($"ListEntriesMode '{mode.GetType().Name}' is not supported.");
        }
    }

    public Task<bool> FileExists(FsPath path, CancellationToken cancellationToken = default)
    {
        var dir = Root.FindDirectory(path.DirectoryPath)
            ?? throw new FileSystemException.NotFound(path.Value);
        return Task.FromResult(dir.GetFile(path.FileName.ToString()) is not null);
    }

    public Task<FileEntry.FileRW> Integrate(FileEntry.IReadableFile file, CancellationToken cancellationToken = default)
    {
        var dir = CreateDirectorySync(file.Path.DirectoryPath, false).treeDir;
        if (dir.ReadOnly)
            throw new FileSystemException.ReadOnly(file.Path.Value);

        var fileCow = file.Inner.CopyOnWrite(() => new MemoryStream(8192));

        var treeFile = dir.GetFile(file.Path.FileName.ToString());
        if (treeFile is null)
            treeFile = dir.CreateFile(file.Path.FileName.ToString(), fileCow);
        else
            treeFile.SwapStream(fileCow);

        return Task.FromResult(new FileEntry.FileRW(treeFile.Path(), this, treeFile.Stream.ZeroPos().Mirror()));
    }

    public Task<FileEntry.FileRO?> OpenFileRead(FsPath path, CancellationToken cancellationToken = default)
    {
        var dir = Root.FindDirectory(path.DirectoryPath)
            ?? throw new FileSystemException.NotFound(path.Value);
        var file = dir.GetFile(path.FileName.ToString());
        if (file is null)
            return Task.FromResult<FileEntry.FileRO?>(null);
        return Task.FromResult(new FileEntry.FileRO(file.Path(), this, file.Stream.ZeroPos().Mirror().ReadOnly()))!;
    }

    public Task<FileEntry.FileRW?> OpenFileReadWrite(FsPath path, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(path.Value);
        var dir = CreateDirectorySync(path.DirectoryPath, false).treeDir;
        if (dir.ReadOnly)
            throw new FileSystemException.ReadOnly(path.Value);
        var file = dir.GetFile(path.FileName.ToString());
        if (file is null)
            return CreateFile(path, cancellationToken)!;
        return Task.FromResult(new FileEntry.FileRW(file.Path(), this, file.Stream.ZeroPos().Mirror()))!;
    }

    public Task<FileEntry.FileWO?> OpenFileWrite(FsPath path, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(path.Value);
        var dir = Root.FindDirectory(path.DirectoryPath)
            ?? throw new FileSystemException.NotFound(path.Value);
        if (dir.ReadOnly)
            throw new FileSystemException.ReadOnly(path.Value);
        var file = dir.GetFile(path.FileName.ToString());
        if (file is null)
        {
            var createdFile = dir.CreateFile(path.FileName.ToString(), new MemoryStream(8192));
            return Task.FromResult(new FileEntry.FileWO(createdFile.Path(), this, createdFile.Stream.ZeroPos().Mirror().WriteOnly()))!;
        }
        var stream = file.Stream;
        if (stream.IsWritable is false)
            throw new FileSystemException.Forbidden(file.Path().Value);
        return Task.FromResult(new FileEntry.FileWO(file.Path(), this, stream.ZeroPos().Mirror().WriteOnly()))!;
    }

    public async Task<FileStatus> FileStat(FsPath path, CancellationToken cancellationToken = default)
    {
        if (Root.IsTombstoned(path))
            return FileStatus.Deleted;
        return await FileExists(path, cancellationToken)
            ? FileStatus.Exists
            : FileStatus.NotFound;
    }
}
