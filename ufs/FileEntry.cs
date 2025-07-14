using System.Buffers;
using System.Text;

namespace ufs;

public abstract record class FileEntry(FsPath Path, IFileSystem Fs)
{
    interface IWritableFile
    {
        FsPath Path { get; }
        IFileSystem Fs { get; }
        StreamWrapper Inner { get; }

        async Task Write(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Inner.WriteAsync(buffer, cancellationToken);
        }

        async Task WriteAllText(string text, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            encoding ??= Encoding.UTF8;
            var bytes = encoding.GetBytes(text);
            using var memOwner = MemoryPool<byte>.Shared.Rent(bytes.Length);
            var buffer = memOwner.Memory[..bytes.Length];
            bytes.CopyTo(buffer.Span);
            await Inner.WriteAsync(buffer, cancellationToken);
        }
    }
    interface IReadableFile
    {
        FsPath Path { get; }
        IFileSystem Fs { get; }
        StreamWrapper Inner { get; }

        async Task<int> Read(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return await Inner.ReadAsync(buffer, cancellationToken);
        }

        async Task<Memory<byte>> ReadAllBytes(CancellationToken cancellationToken = default)
        {
            var len = Inner.Length;
            var buffer = new byte[len].AsMemory();

            int bytesRead = await Inner.ReadAsync(buffer, cancellationToken);
            if (bytesRead < len)
                buffer = buffer[..bytesRead];
            return buffer;
        }

        async Task<string> ReadAllText(Encoding? encoding = null, CancellationToken cancellationToken = default)
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

    public record class FileRW(FsPath Path, IFileSystem Fs, StreamWrapper Inner)
        : FileEntry(Path, Fs), IReadableFile, IWritableFile;
    public record class FileRO(FsPath Path, IFileSystem Fs, StreamWrapper Inner)
        : FileEntry(Path, Fs), IReadableFile;
    public record class FileWO(FsPath Path, IFileSystem Fs, StreamWrapper Inner)
        : FileEntry(Path, Fs), IWritableFile;

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
    }

    public record class Directory(FsPath Path, IFileSystem Fs) : FileEntry(Path, Fs)
    {
        public readonly IFileSystem At = Fs.At(Path);
    }
}
