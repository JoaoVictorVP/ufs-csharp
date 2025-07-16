using System.Buffers;
using System.Text;

namespace ufs;

public abstract record class FileEntry(FsPath Path, IFileSystem Fs) : IDisposable
{
    public abstract void Dispose();

    public interface IWritableFile
    {
        FsPath Path { get; }
        IFileSystem Fs { get; }
        StreamWrapper Inner { get; }

        async Task Write(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            await Inner.Flush(cancellationToken).ConfigureAwait(false);
        }

        public async Task WriteAllText(string text, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            encoding ??= Encoding.UTF8;
            var bytes = encoding.GetBytes(text);
            using var memOwner = MemoryPool<byte>.Shared.Rent(bytes.Length);
            var buffer = memOwner.Memory[..bytes.Length];
            bytes.CopyTo(buffer.Span);
            await Inner.WriteAsync(buffer, cancellationToken);
            await Inner.Flush(cancellationToken).ConfigureAwait(false);
        }

        public async Task Flush(CancellationToken cancellationToken = default)
        {
            await Inner.Flush(cancellationToken).ConfigureAwait(false);
        }
    }
    public interface IReadableFile
    {
        FsPath Path { get; }
        IFileSystem Fs { get; }
        StreamWrapper Inner { get; }

        async Task<int> Read(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return await Inner.ReadAsync(buffer, cancellationToken);
        }

        public async Task<Memory<byte>> ReadAllBytes(CancellationToken cancellationToken = default)
        {
            var len = Inner.Length;
            var buffer = new byte[len].AsMemory();

            int bytesRead = await Inner.ReadAsync(buffer, cancellationToken);
            if (bytesRead < len)
                buffer = buffer[..bytesRead];
            return buffer;
        }

        public async Task<string> ReadAllText(Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            encoding ??= Encoding.UTF8;
            var len = Inner.Length;
            using var memOwner = MemoryPool<byte>.Shared.Rent((int)len);
            var buffer = memOwner.Memory[..(int)len];

            int bytesRead = await Inner.ReadAsync(buffer, cancellationToken);
            if (bytesRead < len)
                buffer = buffer[..bytesRead];
            var text = encoding.GetString(buffer.Span);
            return text;
        }
    }

    public abstract record FileWithStream(FsPath Path, IFileSystem Fs, StreamWrapper Inner) : FileEntry(Path, Fs)
    {
        public override void Dispose()
        {
            GC.SuppressFinalize(this);
            Inner.Dispose();
        }
    }

    public record class FileRW(FsPath Path, IFileSystem Fs, StreamWrapper Inner)
        : FileWithStream(Path, Fs, Inner), IReadableFile, IWritableFile
    {
        public FileRO ReadOnly()
            => new(Path, Fs, Inner.ReadOnly());
        public FileWO WriteOnly()
            => new(Path, Fs, Inner.WriteOnly());
    }
    public record class FileRO(FsPath Path, IFileSystem Fs, StreamWrapper Inner)
        : FileWithStream(Path, Fs, Inner), IReadableFile;
    public record class FileWO(FsPath Path, IFileSystem Fs, StreamWrapper Inner)
        : FileWithStream(Path, Fs, Inner), IWritableFile;

    public record class FileRef(FsPath Path, IFileSystem Fs) : FileEntry(Path, Fs)
    {
        public Task<FileRW?> OpenReadWrite(CancellationToken cancellationToken = default)
            => Fs.OpenFileReadWrite(Path, cancellationToken);
        public Task<FileRO?> OpenRead(CancellationToken cancellationToken = default)
            => Fs.OpenFileRead(Path, cancellationToken);
        public Task<FileWO?> OpenWrite(CancellationToken cancellationToken = default)
            => Fs.OpenFileWrite(Path, cancellationToken);
        public Task<bool> Exists(CancellationToken cancellationToken = default)
            => Fs.FileExists(Path, cancellationToken);
        public Task<bool> Delete(CancellationToken cancellationToken = default)
            => Fs.DeleteFile(Path, cancellationToken);
        
        public override void Dispose()
            => GC.SuppressFinalize(this);
    }

    public record class Directory(FsPath Path, IFileSystem Fs) : FileEntry(Path, Fs)
    {
        private IFileSystem? at;
        public IFileSystem At()
        {
            return at ??= Path switch
            {
                { IsRoot: true } => Fs,
                _ => Fs.At(Path, FileSystemMode.Inherit)
            };
        }

        public override void Dispose()
            => GC.SuppressFinalize(this);
    }
}
public static class FileEntryExtensions
{
    public static async Task<string> ReadAllText(this FileEntry.IReadableFile file, Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        return await file.ReadAllText(encoding, cancellationToken);
    }
    public static async Task<Memory<byte>> ReadAllBytes(this FileEntry.IReadableFile file, CancellationToken cancellationToken = default)
    {
        return await file.ReadAllBytes(cancellationToken);
    }

    public static async Task WriteAllText(this FileEntry.IWritableFile file, string text, Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        await file.WriteAllText(text, encoding, cancellationToken);
    }
    public static async Task Flush(this FileEntry.IWritableFile file, CancellationToken cancellationToken = default)
    {
        await file.Flush(cancellationToken);
    }
}
