using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Minio;
using Minio.DataModel.Args;

namespace ufs.minio;

public record MinioFileSystem(string Root, MinioClientWrapper Client, bool ReadOnly = false) : IFileSystem
{
    private MinioFileSystem? parent;
    private ConcurrentDictionary<FsPath, bool>? selfDirectories;
    private ConcurrentDictionary<FsPath, bool>? selfFiles;

    private ConcurrentDictionary<FsPath, bool> Directories =>
        parent?.Directories ?? (selfDirectories ??= []);
    private ConcurrentDictionary<FsPath, bool> Files =>
        parent?.Files ?? (selfFiles ??= []);

    FsPath CompletePath(FsPath path, string[]? completing = null)
    {
        if (parent is not null)
            return parent.CompletePath(path, [..completing ?? [], Root]);
        var pathStr = path.Value.AsSpan()[1..];
        var full = Path.Combine([Root, ..completing ?? [], pathStr.ToString()]);
        if (full.StartsWith('/') is false)
            full = '/' + full;
        return full.FsPath();
    }

    string RealPath(FsPath path)
    {
        var final = Path.Combine(Root, path.Value[1..]);
        if (final.StartsWith('/'))
            final = final[1..];
        return final;
    }

    public IFileSystem At(FsPath path, FileSystemMode mode = FileSystemMode.Inherit)
    {
        var nextReadOnly = mode switch
        {
            FileSystemMode.Inherit => ReadOnly,
            FileSystemMode.ReadOnly => true,
            FileSystemMode.ReadWrite => false,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
        if (nextReadOnly is false && ReadOnly)
            throw new FileSystemException.ReadOnly(Root);

        var nextRoot = RealPath(path);
        var nfs = new MinioFileSystem(nextRoot, Client, nextReadOnly)
        {
            parent = this
        };
        return nfs;
    }

    public Task<FileEntry.Directory> CreateDirectory(FsPath path, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(Root);
        Directories.TryAdd(CompletePath(path), true);
        return Task.FromResult(new FileEntry.Directory(path, this));
    }

    public Task<FileEntry.FileRW> CreateFile(FsPath path, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(Root);
        Files.TryAdd(CompletePath(path), true);
        var backingStream = new MemoryStream(8192);
        var objectName = RealPath(path);
        var stream = new MinioReadWriteStream(Client, objectName, backingStream);
        return Task.FromResult(new FileEntry.FileRW(path, this, stream));
    }

    public async Task<bool> DeleteDirectory(FsPath path, bool recursive = false, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(Root);
        var dirCompletePath = CompletePath(path);
        int ack = 0;
        if (Directories.TryRemove(dirCompletePath, out _))
            ack++;
        var objectName = RealPath(path);
        var entries = Client.ListObjects(prefix: objectName, recursive, cancellationToken);
        await foreach (var (entry, isDir) in entries)
        {
            if (isDir)
                continue;
            ack += await Client.Delete(entry).ConfigureAwait(false) ? 1 : 0;
        }
        foreach (var file in Files)
        {
            if (file.Key.InDirectory(dirCompletePath))
            {
                Files.TryRemove(file.Key, out _);
                ack++;
            }
        }
        foreach (var dir in Directories)
        {
            if (dir.Key.InDirectory(dirCompletePath))
            {
                Directories.TryRemove(dir.Key, out _);
                ack++;
            }
        }
        return ack > 0;
    }

    public Task<bool> DeleteFile(FsPath path, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(Root);
        Files.TryRemove(CompletePath(path), out _);
        var objectName = RealPath(path);
        return Client.Delete(objectName);
    }

    public async Task<bool> DirExists(FsPath path, CancellationToken cancellationToken = default)
    {
        if (Directories.TryGetValue(CompletePath(path), out _))
            return true;
        var objectName = RealPath(path);
        var entries = Client.ListObjects(prefix: objectName, recursive: false, cancellationToken);
        await foreach (var _ in entries)
            return true;
        return false;
    }

    public async IAsyncEnumerable<FileEntry> Entries(FsPath path, ListEntriesMode mode, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var directoryObjectName = RealPath(path);
        var prefixFilter = mode.Filter switch
        {
            [.. var begin, '*'] => begin,
            _ => ""
        };
        var prefixQuery = directoryObjectName + prefixFilter;
        var recursive = mode is ListEntriesMode.RecursiveMode;
        var entries = Client.ListObjects(prefix: prefixQuery, recursive, cancellationToken);
        var filterPattern = FileUtils.GlobFilterToPattern(mode.Filter);
        var consumedEntries = new HashSet<FsPath>();
        await foreach (var (entryIdeal, isDir) in entries)
        {
            var entry = entryIdeal;
            if (Regex.IsMatch(entry, filterPattern) is false)
                continue;
            if (entry.StartsWith(Root))
                entry = entry[Root.Length..];
            if (entry.StartsWith('/') is false)
                entry = '/' + entry;
            var fsPath = entry.FsPath();
            consumedEntries.Add(fsPath);
            if (isDir)
                yield return new FileEntry.Directory(fsPath, this);
            else
                yield return new FileEntry.FileRef(fsPath, this);
        }
        
        bool IsSubdirectory(FsPath dir)
        {
            var dirStr = dir.Value.AsSpan();
            if(dirStr[0] is '/')
                dirStr = dirStr[1..];
            if (dirStr.StartsWith(directoryObjectName))
            {
                int size = directoryObjectName.Length;
                var subPath = dirStr[size..];
                if(subPath is "")
                    return false;
                if (subPath[0] is '/')
                    subPath = subPath[1..];
                return subPath.Contains('/');
            }
            return false;
        }
        bool IsSelf(FsPath dir)
        {
            var dirStr = dir.Value.AsSpan();
            if(dirStr[0] is '/')
                dirStr = dirStr[1..];
            return dirStr.SequenceEqual(directoryObjectName.AsSpan());
        }
        foreach (var dir in Directories.Keys)
        {
            if (consumedEntries.Contains(dir))
                continue;
            if (recursive is false && IsSubdirectory(dir))
                continue;
            if (IsSelf(dir))
                continue;
            if (Regex.IsMatch(dir.Value, filterPattern) is false)
                continue;
            yield return new FileEntry.Directory(dir, this);
        }
        foreach (var file in Files.Keys)
        {
            if (consumedEntries.Contains(file))
                continue;
            if (recursive is false && IsSubdirectory(file))
                continue;
            if (Regex.IsMatch(file.Value, filterPattern) is false)
                continue;
            yield return new FileEntry.FileRef(file, this);
        }
    }

    public Task<bool> FileExists(FsPath path, CancellationToken cancellationToken = default)
    {
        if (Files.TryGetValue(CompletePath(path), out _))
            return Task.FromResult(true);
        var objectName = RealPath(path);
        return Client.Exists(objectName);
    }

    public Task<FileStatus> FileStat(FsPath path, CancellationToken cancellationToken = default)
    {
        if (Files.TryGetValue(CompletePath(path), out _))
            return Task.FromResult(FileStatus.Exists);
        var objectName = RealPath(path);
        return Client.Stat(objectName);
    }

    public async Task<FileEntry.FileRW> Integrate(FileEntry.IReadableFile file, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(Root);
        var selfFile = await CreateFile(file.Path, cancellationToken).ConfigureAwait(false);
        await file.Inner.CopyToAsync(selfFile.Inner, cancellationToken).ConfigureAwait(false);
        await selfFile.Inner.Flush(cancellationToken).ConfigureAwait(false);
        selfFile.Inner.Position = 0;
        return selfFile;
    }

    public async Task<FileEntry.FileRO?> OpenFileRead(FsPath path, CancellationToken cancellationToken = default)
    {
        var objectName = RealPath(path);
        if (await Client.Exists(objectName).ConfigureAwait(false) is false)
        {
            if(Files.TryGetValue(CompletePath(path), out _))
                return new FileEntry.FileRO(path, this, StreamWrapper.Null.ReadOnly());
            return null;
        }
        var (stream, length) = await Client.Get(objectName).ConfigureAwait(false);
        if (stream == Stream.Null)
            return null;
        var minioStream = new MinioReadStream(stream, length);
        return new FileEntry.FileRO(path, this, minioStream);
    }

    public async Task<FileEntry.FileRW?> OpenFileReadWrite(FsPath path, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(Root);
        var objectName = RealPath(path);
        var minioStream = new MinioReadWriteStream(Client, objectName, new MemoryStream(8192));
        await minioStream.FetchContents().ConfigureAwait(false);
        Files.TryAdd(CompletePath(path), true);
        return new FileEntry.FileRW(path, this, minioStream);
    }

    public async Task<FileEntry.FileWO?> OpenFileWrite(FsPath path, CancellationToken cancellationToken = default)
    {
        if (ReadOnly)
            throw new FileSystemException.ReadOnly(Root);
        if (await FileExists(path, cancellationToken).ConfigureAwait(false) is false)
            return null;
        var objectName = RealPath(path);
        var minioStream = new MinioWriteStream(Client, objectName, new MemoryStream(8192));
        return new FileEntry.FileWO(path, this, minioStream);
    }
}
