using System.Buffers;

namespace ufs;

public abstract record class StreamWrapper : IDisposable
{
    public abstract bool IsReadable { get; }
    public abstract bool IsWritable { get; }
    public abstract long Length { get; }

    public abstract long Position { get; }

    public abstract Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
    public abstract Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);

    public abstract Task CopyToAsync(StreamWrapper destination, CancellationToken cancellationToken = default);

    public abstract Task Flush(CancellationToken cancellationToken = default);

    public abstract void Dispose();

    public Cow CopyOnWrite(Func<StreamWrapper> create)
    {
        return new Cow(this, create);
    }

    public record Real(Stream Inner) : StreamWrapper
    {
        public override bool IsReadable => Inner.CanRead;
        public override bool IsWritable => Inner.CanWrite;
        public override long Length => Inner.Length;

        public override long Position => Inner.Position;

        public override async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var bytesRead = await Inner.ReadAsync(buffer, cancellationToken);
            return bytesRead;
        }
        public override async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Inner.WriteAsync(buffer, cancellationToken);
        }
        public override async Task CopyToAsync(StreamWrapper destination, CancellationToken cancellationToken = default)
        {
            if (destination is Real dest)
            {
                await Inner.CopyToAsync(dest.Inner, cancellationToken);
                return;
            }
            using var bufferOwner = MemoryPool<byte>.Shared.Rent(81920);
            var buffer = bufferOwner.Memory;
            int bytesRead;
            while ((bytesRead = await Inner.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.Slice(0, bytesRead), cancellationToken);
            }
        }

        public override void Dispose()
        {
            Inner.Dispose();
        }

        public override Task Flush(CancellationToken cancellationToken = default)
        {
            return Inner.FlushAsync(cancellationToken);
        }
    };
    public record Functional(
        Func<bool> isReadable,
        Func<bool> isWritable,
        Func<long> length,
        Func<long> position,
        Func<Memory<byte>, CancellationToken, Task<int>> readAsync,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> writeAsync,
        Action dispose,
        Func<Task>? flushAsync = null
    ) : StreamWrapper
    {
        private readonly Func<bool> isReadable = isReadable;
        private readonly Func<bool> isWritable = isWritable;
        private readonly Func<long> length = length;
        private readonly Func<long> position = position;
        private readonly Func<Memory<byte>, CancellationToken, Task<int>> readAsync = readAsync;
        private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> writeAsync = writeAsync;
        private readonly Func<Task>? flushAsync = flushAsync;
        private readonly Action dispose = dispose;

        public override bool IsReadable => isReadable();
        public override bool IsWritable => isWritable();
        public override long Length => length();

        public override long Position => position();

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

        public override async Task CopyToAsync(StreamWrapper destination, CancellationToken cancellationToken = default)
        {
            using var bufferOwner =
                MemoryPool<byte>.Shared.Rent(81920);
            var buffer = bufferOwner.Memory;
            int bytesRead;
            while ((bytesRead = await ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer[..bytesRead], cancellationToken);
            }
        }

        public override Task Flush(CancellationToken cancellationToken = default)
        {
            return flushAsync?.Invoke() ?? Task.CompletedTask;
        }
    }
    public record Cow(StreamWrapper Origin, Func<StreamWrapper> Create) : StreamWrapper
    {
        private StreamWrapper Inner { get; set; } = Origin;

        public override bool IsReadable => Inner.IsReadable;
        public override bool IsWritable => Inner.IsWritable;
        public override long Length => Inner.Length;

        public override long Position => Inner.Position;

        public override Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return Inner.ReadAsync(buffer, cancellationToken);
        }
        public override async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Inner == Origin)
            {
                var next = Create();
                Inner = next;
                if (next.IsWritable is false)
                    throw new InvalidOperationException("Cannot write to a read-only stream.");
                await Origin.CopyToAsync(next, cancellationToken);
            }
            await Inner.WriteAsync(buffer, cancellationToken);
        }

        public override async Task CopyToAsync(StreamWrapper destination, CancellationToken cancellationToken = default)
        {
            var dest = new byte[81920];
            int bytesRead;
            while ((bytesRead = await Inner.ReadAsync(dest, cancellationToken)) > 0)
            {
                await destination.WriteAsync(dest.AsMemory(0, bytesRead), cancellationToken);
            }
        }

        public override Task Flush(CancellationToken cancellationToken = default)
        {
            return Inner.Flush(cancellationToken);
        }

        public override void Dispose()
        {
            GC.SuppressFinalize(this);
            if(Inner == Origin)
                return;
            Inner.Dispose();
        }
    }

    public static implicit operator StreamWrapper(Stream real) => new Real(real);

    public BackedStream GetBackedStream()
    {
        return new BackedStream(this);
    }
    public class BackedStream(StreamWrapper wrapper) : Stream
    {
        private readonly StreamWrapper wrapper = wrapper;

        public override bool CanRead => wrapper.IsReadable;

        public override bool CanSeek => false;

        public override bool CanWrite => wrapper.IsWritable;

        public override long Length => wrapper.Length;

        public override long Position
        {
            get => wrapper.Position;
            set => throw new NotImplementedException();
        }

        public override void Flush()
        {
            wrapper.Flush().GetAwaiter().GetResult();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return wrapper.ReadAsync(new Memory<byte>(buffer, offset, count)).GetAwaiter().GetResult();
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return await wrapper.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("Seek is not supported on this stream.");
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("SetLength is not supported on this stream.");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            wrapper.WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count)).GetAwaiter().GetResult();
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await wrapper.WriteAsync(buffer, cancellationToken);
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return wrapper.Flush(cancellationToken);
        }
        public override ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            wrapper.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
