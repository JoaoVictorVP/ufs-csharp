
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
        var (treeDir, _) = CreateDirectorySync(path.DirectoryPath, nextReadOnly);
        return new MemoryFileSystem(treeDir);
    }

    (MemoryFileTree.Directory treeDir, FileEntry.Directory dir) CreateDirectorySync(FsPath path, bool readOnly = false)
    {
        var lastExistingDir = Root.RecursiveWalk(path)
            .Where(dir => dir is not null)
            .LastOrDefault();
        if (lastExistingDir is null)
            throw new FileSystemException.NotFound(path.Value);
        if (lastExistingDir.ReadOnly)
            throw new FileSystemException.ReadOnly(path.Value);
        var directory =
            lastExistingDir.Path() == path
                ? lastExistingDir
                : lastExistingDir.CreateDir(path.FileName.ToString(), readOnly);
        return (directory, new FileEntry.Directory(directory.Path(), this));
    }
    public Task<FileEntry.Directory> CreateDirectory(FsPath path, CancellationToken cancellationToken = default)
    {
        var (_, dir) = CreateDirectorySync(path, false);
        return Task.FromResult(dir);
    }

    public Task<FileEntry.FileRW> CreateFile(FsPath path, CancellationToken cancellationToken = default)
    {
        _ = path.Resolve(Root.Path().Value, Root.Path().Value)
            ?? throw new PathException.InvalidPath(path.Value);
        var dir = Root.RecursiveWalk(path.DirectoryPath)
            .Where(dir => dir is not null && dir.Path() == path.DirectoryPath)
            .LastOrDefault()
            ?? throw new FileSystemException.NotFound(path.Value);
        if (dir.ReadOnly)
            throw new FileSystemException.ReadOnly(path.Value);
        var file = dir.CreateFile(path.FileName.ToString(), new MemoryStream(8192));
        return Task.FromResult(new FileEntry.FileRW(file.Path(), this, file.Stream));
    }

    public Task<bool> DeleteDirectory(FsPath path, bool recursive = false, CancellationToken cancellationToken = default)
    {
        var (treeDir, _) = CreateDirectorySync(path.DirectoryPath, false);
        if (treeDir is null)
            throw new FileSystemException.NotFound(path.Value);
        if (treeDir.ReadOnly)
            throw new FileSystemException.ReadOnly(path.Value);
        Root.DeleteDir(treeDir);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteFile(FsPath path, CancellationToken cancellationToken = default)
    {
        var dir = Root.RecursiveWalk(path.DirectoryPath)
            .Where(dir => dir is not null && dir.Path() == path.DirectoryPath)
            .LastOrDefault();
        if (dir is null)
            throw new FileSystemException.NotFound(path.Value);
        if (dir.ReadOnly)
            throw new FileSystemException.ReadOnly(path.Value);
        var file = dir.GetFile(path.FileName.ToString());
        if (file is null)
        {
            Root.TombstoneFile(path);
            return Task.FromResult(true);
        }
        Root.DeleteFile(file);
        return Task.FromResult(true);
    }

    public Task<bool> DirExists(FsPath path, CancellationToken cancellationToken = default)
    {
        var dir = Root.RecursiveWalk(path)
            .Where(dir => dir is not null && dir.Path() == path)
            .LastOrDefault();
        return Task.FromResult(dir is not null);
    }

    public async IAsyncEnumerable<FileEntry> Entries(FsPath path, ListEntriesMode mode, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var localRoot = Root.RecursiveWalk(path)
            .Where(dir => dir is not null && dir.Path() == path)
            .LastOrDefault();
        if (localRoot is null)
            throw new FileSystemException.NotFound(path.Value);

        static string ToPattern(string filter)
        {
            if (filter is "" or "*")
                return @".*";
            return filter.Replace(".", @"\.")
                .Replace("*", @".*")
                .Replace("?", @".?")
                .Replace("[", @"\[")
                .Replace("]", @"\]")
                .Replace("(", @"\(")
                .Replace(")", @"\)")
                .Replace("{", @"\{")
                .Replace("}", @"\}")
                .Replace("+", @"\+")
                .Replace("$", @"\$")
                .Replace("^", @"\^")
                .Replace("|", @"\|")
                .Replace("~", @"\~")
                .Replace("`", @"\`")
                .Replace("=", @"\=")
                .Replace("!", @"\!")
                .Replace(">", @"\>")
                .Replace("<", @"\<")
                .Replace("&", @"\&")
                .Replace("'", @"\'")
                .Replace("\"", @"\""")
                .Replace(";", @"\;")
                .Replace(",", @"|")
                .Replace("/", @"\/");
        }

        switch (mode)
        {
            case ListEntriesMode.ShallowMode shallow:
                foreach (var dir in localRoot.Directories.Values)
                {
                    if (shallow.IsAll || Regex.IsMatch(dir.Name.ToString(), ToPattern(shallow.Filter), RegexOptions.IgnoreCase))
                        yield return new FileEntry.Directory(dir.Path(), this);
                }
                foreach (var file in localRoot.Files.Values)
                {
                    if (shallow.IsAll || Regex.IsMatch(file.Name.ToString(), ToPattern(shallow.Filter), RegexOptions.IgnoreCase))
                        yield return new FileEntry.FileRef(file.Path(), this);
                }
                break;
            case ListEntriesMode.RecursiveMode recursive:
                foreach (var entry in localRoot.RecursiveEntries())
                {
                    switch (entry)
                    {
                        case MemoryFileTree.Directory dir when recursive.IsAll || Regex.IsMatch(dir.Name.ToString(), ToPattern(recursive.Filter), RegexOptions.IgnoreCase):
                            yield return new FileEntry.Directory(dir.Path(), this);
                            break;
                        case MemoryFileTree.File file when recursive.IsAll || Regex.IsMatch(file.Name.ToString(), ToPattern(recursive.Filter), RegexOptions.IgnoreCase):
                            yield return new FileEntry.FileRef(file.Path(), this);
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
        var dir = Root.RecursiveWalk(path.DirectoryPath)
            .Where(dir => dir is not null)
            .LastOrDefault();
        if (dir is null)
            throw new FileSystemException.NotFound(path.Value);
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

        return Task.FromResult(new FileEntry.FileRW(treeFile.Path(), this, treeFile.Stream));
    }

    public Task<FileEntry.FileRO?> OpenFileRead(FsPath path, CancellationToken cancellationToken = default)
    {
        var dir = Root.RecursiveWalk(path.DirectoryPath)
            .Where(dir => dir is not null)
            .LastOrDefault();
        if (dir is null)
            throw new FileSystemException.NotFound(path.Value);
        var file = dir.GetFile(path.FileName.ToString());
        if (file is null)
            return Task.FromResult<FileEntry.FileRO?>(null);
        return Task.FromResult(new FileEntry.FileRO(file.Path(), this, file.Stream))!;
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
        return Task.FromResult(new FileEntry.FileRW(file.Path(), this, file.Stream))!;
    }

    public Task<FileEntry.FileWO?> OpenFileWrite(FsPath path, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(path.Value);
        var dir = Root.RecursiveWalk(path.DirectoryPath)
            .Where(dir => dir is not null)
            .LastOrDefault();
        if (dir is null)
            throw new FileSystemException.NotFound(path.Value);
        if (dir.ReadOnly)
            throw new FileSystemException.ReadOnly(path.Value);
        var file = dir.GetFile(path.FileName.ToString());
        if (file is null)
        {
            var createdFile = dir.CreateFile(path.FileName.ToString(), new MemoryStream(8192));
            return Task.FromResult(new FileEntry.FileWO(createdFile.Path(), this, createdFile.Stream))!;
        }
        var stream = file.Stream;
        if (stream.IsWritable is false)
            throw new FileSystemException.Forbidden(file.Path().Value);
        return Task.FromResult(new FileEntry.FileWO(file.Path(), this, stream))!;
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
