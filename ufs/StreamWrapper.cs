namespace ufs;

public abstract record class StreamWrapper : IDisposable
{
    public abstract bool IsReadable { get; }
    public abstract bool IsWritable { get; }
    public abstract long Length { get; }

    public abstract Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
    public abstract Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);

    public abstract void Dispose();

    public record Real(Stream Inner) : StreamWrapper
    {
        public override bool IsReadable => Inner.CanRead;
        public override bool IsWritable => Inner.CanWrite;
        public override long Length => Inner.Length;

        public override async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var bytesRead = await Inner.ReadAsync(buffer, cancellationToken);
            return bytesRead;
        }
        public override async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Inner.WriteAsync(buffer, cancellationToken);
        }

        public override void Dispose()
        {
            Inner.Dispose();
        }
    };
    public record Functional(
        Func<bool> isReadable,
        Func<bool> isWritable,
        Func<long> length,
        Func<Memory<byte>, CancellationToken, Task<int>> readAsync,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> writeAsync,
        Action dispose
    ) : StreamWrapper
    {
        private readonly Func<bool> isReadable = isReadable;
        private readonly Func<bool> isWritable = isWritable;
        private readonly Func<long> length = length;
        private readonly Func<Memory<byte>, CancellationToken, Task<int>> readAsync = readAsync;
        private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> writeAsync = writeAsync;
        private readonly Action dispose = dispose;

        public override bool IsReadable => isReadable();
        public override bool IsWritable => isWritable();
        public override long Length => length();

        public override async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return await readAsync(buffer, cancellationToken);
        }
        public override async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await writeAsync(buffer, cancellationToken);
        }

        public override void Dispose()
        {
            GC.SuppressFinalize(this);
            dispose();
        }
    }
    
    public static implicit operator StreamWrapper(Stream real) => new Real(real);
}
