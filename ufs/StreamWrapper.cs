using System.Buffers;

namespace ufs;

public abstract record class StreamWrapper : IDisposable
{
    public static readonly StreamWrapper Null = Stream.Null;

    public abstract bool IsReadable { get; }
    public abstract bool IsWritable { get; }
    public abstract long Length { get; }

    public abstract long Position { get; set; }

    public abstract bool Owned { get; }

    public abstract void SetLength(long value);

    public abstract Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
    public abstract Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);

    public abstract Task CopyToAsync(StreamWrapper destination, CancellationToken cancellationToken = default);

    public abstract Task Flush(CancellationToken cancellationToken = default);

    public abstract void Dispose();

    public Cow CopyOnWrite(Func<StreamWrapper> create)
        => new(this, create);
    
    public StreamWrapper ZeroPos()
    {
        Position = 0;
        return this;
    }

    public MirrorStream Mirror()
        => new(this);
    
    public ReadOnlyStream ReadOnly()
        => new(this);
    
    public WriteOnlyStream WriteOnly()
        => new(this);

    public WriteLimitedStream WriteLimited(long maxWrittenSize)
        => new(this, maxWrittenSize);

    public Real Buffered(int bufferSize = 4096)
        => new(GetBackedStream().Buffered(bufferSize));

    public Real Synchronized()
        => new(Stream.Synchronized(GetBackedStream()));

    public async Task<Real> IntoMemory()
    {
        var mem = new MemoryStream((int)Length);
        await CopyToAsync(mem).ConfigureAwait(false);
        mem.Position = 0;
        return new Real(mem);
    }

    public record Real(Stream Inner) : StreamWrapper
    {
        public override bool IsReadable => Inner.CanRead;
        public override bool IsWritable => Inner.CanWrite;
        public override long Length => Inner.Length;

        public override long Position
        {
            get => Inner.Position;
            set
            {
                if (disposed)
                    return;
                Inner.Position = value;
            }
        }

        public override bool Owned => true;

        public override void SetLength(long value)
        {
            if (disposed)
                return;
            Inner.SetLength(value);
        }

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

        bool disposed;
        public override void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            GC.SuppressFinalize(this);
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
        Action<long>? setPosition,
        Func<Memory<byte>, CancellationToken, Task<int>> readAsync,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> writeAsync,
        Action dispose,
        Action<long>? setLength = null,
        Func<Task>? flushAsync = null
    ) : StreamWrapper
    {
        private readonly Func<bool> isReadable = isReadable;
        private readonly Func<bool> isWritable = isWritable;
        private readonly Func<long> length = length;
        private readonly Func<long> position = position;
        private readonly Action<long>? setPosition = setPosition;
        private readonly Func<Memory<byte>, CancellationToken, Task<int>> readAsync = readAsync;
        private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> writeAsync = writeAsync;
        private readonly Func<Task>? flushAsync = flushAsync;
        private readonly Action dispose = dispose;
        private readonly Action<long>? setLength = setLength;

        public override bool IsReadable => isReadable();
        public override bool IsWritable => isWritable();
        public override long Length => length();

        public override bool Owned => false;

        public override long Position
        {
            get => position();
            set => setPosition?.Invoke(value);
        }

        public override void SetLength(long value)
            => setLength?.Invoke(value);

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
        public override bool IsWritable => true;
        public override long Length => Inner.Length;

        public override long Position
        {
            get => Inner.Position;
            set => Inner.Position = value;
        }

        public override bool Owned => Inner != Origin;

        public override void SetLength(long value)
        {
            if (Inner == Origin)
            {
                var next = Create();
                next.Position = Inner.Position;
                Inner = next;
                if (next.IsWritable is false)
                    throw new InvalidOperationException("Cannot set length on a read-only stream.");
            }
            Inner.SetLength(value);
        }

        public override Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return Inner.ReadAsync(buffer, cancellationToken);
        }
        public override async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Inner == Origin)
            {
                var next = Create();
                long ogPos = Inner.Position;
                Inner = next;
                if (next.IsWritable is false)
                    throw new InvalidOperationException("Cannot write to a read-only stream.");
                await Origin.CopyToAsync(next, cancellationToken);
                next.Position = ogPos;
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

    public record MirrorStream(StreamWrapper Inner) : StreamWrapper
    {
        public override bool IsReadable => Inner.IsReadable;
        public override bool IsWritable => Inner.IsWritable;
        public override long Length => Inner.Length;

        public override long Position
        {
            get => Inner.Position;
            set => Inner.Position = value;
        }

        public override bool Owned => false;

        public override void SetLength(long value)
            => Inner.SetLength(value);

        public override Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return Inner.ReadAsync(buffer, cancellationToken);
        }
        public override async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task Flush(CancellationToken cancellationToken = default)
        {
            return Inner.Flush(cancellationToken);
        }

        public override Task CopyToAsync(StreamWrapper destination, CancellationToken cancellationToken = default)
        {
            return Inner.CopyToAsync(destination, cancellationToken);
        }

        public override void Dispose()
        {
            GC.SuppressFinalize(this);
            Position = 0;
        }
    }

    public record ReadOnlyStream(StreamWrapper Inner) : StreamWrapper
    {
        public override bool IsReadable => Inner.IsReadable;
        public override bool IsWritable => false; // Read-only stream
        public override long Length => Inner.Length;

        public override long Position
        {
            get => Inner.Position;
            set
            {
                if (Inner.Position == value)
                    return;
                Inner.Position = value;
            }
        }

        public override bool Owned => Inner.Owned;

        public override void SetLength(long value)
            => throw new NotSupportedException("Cannot set length on a read-only stream.");

        public override Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return Inner.ReadAsync(buffer, cancellationToken);
        }
        
        public override Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Cannot write to a read-only stream.");

        public override Task Flush(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public override void Dispose()
        {
            GC.SuppressFinalize(this);
            Inner.Dispose();
        }

        public override Task CopyToAsync(StreamWrapper destination, CancellationToken cancellationToken = default)
        {
            return Inner.CopyToAsync(destination, cancellationToken);
        }
    }

    public record WriteOnlyStream(StreamWrapper Inner) : StreamWrapper
    {
        public override bool IsReadable => false;
        public override bool IsWritable => Inner.IsWritable;
        public override long Length => Inner.Length;

        public override long Position
        {
            get => Inner.Position;
            set => Inner.Position = value;
        }

        public override bool Owned => Inner.Owned;

        public override void SetLength(long value)
            => Inner.SetLength(value);

        public override Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Cannot read from a write-only stream.");

        public override async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task Flush(CancellationToken cancellationToken = default)
            => Inner.Flush(cancellationToken);

        public override void Dispose()
        {
            GC.SuppressFinalize(this);
            Inner.Dispose();
        }

        public override Task CopyToAsync(StreamWrapper destination, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Cannot copy from a write-only stream.");
        }
    }
    public record WriteLimitedStream(StreamWrapper Inner, long maxWrittenSize) : StreamWrapper
    {
        long written = 0;
        public override bool IsReadable => Inner.IsReadable;
        public override bool IsWritable => Inner.IsWritable && written < maxWrittenSize;
        public override long Length => Inner.Length;

        public override long Position
        {
            get => Inner.Position;
            set => Inner.Position = value;
        }

        public override bool Owned => Inner.Owned;

        public override void SetLength(long value)
        {
            if (value is 0)
                written = 0;
            Inner.SetLength(value);
        }

        public override Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Inner.ReadAsync(buffer, cancellationToken);

        public override async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            written += buffer.Length;
            if (written > maxWrittenSize)
                throw new InvalidOperationException($"Cannot write more than {maxWrittenSize} bytes to this stream.");
            if (Inner.IsWritable is false)
                throw new InvalidOperationException("Cannot write to a read-only stream.");
            await Inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task Flush(CancellationToken cancellationToken = default)
            => Inner.Flush(cancellationToken);

        public override void Dispose()
        {
            GC.SuppressFinalize(this);
            Inner.Dispose();
        }

        public override Task CopyToAsync(StreamWrapper destination, CancellationToken cancellationToken = default)
            => Inner.CopyToAsync(destination, cancellationToken);
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
            set => wrapper.Position = value;
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
