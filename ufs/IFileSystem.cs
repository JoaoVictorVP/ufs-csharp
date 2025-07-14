using System;

namespace ufs;

public interface IFileSystem
{
    bool ReadOnly { get; }

    Task<bool> FileExists(FsPath path, CancellationToken cancellationToken = default);
    Task<bool> DirExists(FsPath path, CancellationToken cancellationToken = default);
    Task<FileEntry.FileRW> CreateFile(FsPath path, CancellationToken cancellationToken = default);
    Task<FileEntry.FileRW?> OpenFileReadWrite(FsPath path, CancellationToken cancellationToken = default);
    Task<FileEntry.FileRO?> OpenFileRead(FsPath path, CancellationToken cancellationToken = default);
    Task<FileEntry.FileWO?> OpenFileWrite(FsPath path, CancellationToken cancellationToken = default);
    Task<FileEntry.Directory> CreateDirectory(FsPath path, CancellationToken cancellationToken = default);
    Task<bool> DeleteFile(FsPath path, CancellationToken cancellationToken = default);
    Task<bool> DeleteDirectory(FsPath path, bool recursive = false, CancellationToken cancellationToken = default);

    IAsyncEnumerable<FileEntry> Entries(FsPath path, ListEntriesMode mode, CancellationToken cancellationToken = default);

    IFileSystem At(FsPath path, FileSystemMode mode = FileSystemMode.Inherit);
}
public enum FileSystemMode
{
    Inherit = 0,
    ReadOnly = 1,
    ReadWrite = 2,
}
public record ListEntriesMode
{
    public static readonly ListEntriesMode ShallowAll = new ShallowMode("*");
    public static readonly ListEntriesMode RecursiveAll = new RecursiveMode("*");
    
    public static ListEntriesMode Shallow(string filter)
        => new ShallowMode(filter);
    public static ListEntriesMode Recursive(string filter)
        => new RecursiveMode(filter);

    public record ShallowMode(string Filter) : ListEntriesMode;
    public record RecursiveMode(string Filter) : ListEntriesMode;
}
