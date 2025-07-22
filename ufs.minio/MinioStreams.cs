using System;
using System.Buffers;

namespace ufs.minio;

public sealed record MinioReadWriteStream(
    MinioClientWrapper Client,
    string ObjectName,
    Stream BackingStream
) : StreamWrapper
{
    public async Task FetchContents()
    {
        try
        {
            var (stream, length) = await Client.Get(ObjectName).ConfigureAwait(false);
            using var _ = stream;
            if (stream == null)
                throw new FileNotFoundException($"Object {ObjectName} not found in bucket {Client.BucketName}");
            if (length == 0)
                throw new FileNotFoundException($"Object {ObjectName} is empty in bucket {Client.BucketName}");
            BackingStream.Position = 0;
            BackingStream.SetLength(0);

            await stream.CopyToAsync(BackingStream).ConfigureAwait(false);
            BackingStream.Position = 0;
        }
        catch
        {
            BackingStream.Position = 0;
            BackingStream.SetLength(0);
        }
    }
    
    public override bool IsReadable => BackingStream.CanRead;

    public override bool IsWritable => BackingStream.CanWrite;

    public override long Length => BackingStream.Length;

    public override long Position
    {
        get => BackingStream.Position;
        set => BackingStream.Position = value;
    }

    public override bool Owned => true;

    public override async Task CopyToAsync(StreamWrapper destination, CancellationToken cancellationToken = default)
    {
        BackingStream.Position = 0;
        using var bufferOwner = MemoryPool<byte>.Shared.Rent(81920);
        var buffer = bufferOwner.Memory;
        int bytesRead;
        while ((bytesRead = BackingStream.Read(buffer.Span)) > 0)
        {
            await destination.WriteAsync(buffer[..bytesRead], cancellationToken).ConfigureAwait(false);
        }
        await destination.Flush(cancellationToken).ConfigureAwait(false);
        BackingStream.Position = 0;
    }

    public override void Dispose()
    {
        BackingStream.Dispose();
    }

    public override async Task Flush(CancellationToken cancellationToken = default)
    {
        var bpos = BackingStream.Position;
        BackingStream.Position = 0;
        await BackingStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        var mime = FileUtils.InferContentType(ObjectName);
        await Client.Upload(ObjectName, BackingStream, mime).ConfigureAwait(false);
        BackingStream.Position = bpos;
    }

    public override Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return BackingStream.ReadAsync(buffer, cancellationToken).AsTask();
    }

    public override void SetLength(long value)
    {
        if (BackingStream.Position > value)
            BackingStream.Position = value;
        BackingStream.SetLength(value);
    }

    public override Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return BackingStream.WriteAsync(buffer, cancellationToken).AsTask();
    }
}

public sealed record MinioReadStream(Stream BackingStream, long BackingLength) : StreamWrapper
{
    public override bool IsReadable => BackingStream.CanRead;

    public override bool IsWritable => false;

    public override long Length => BackingLength;

    public override long Position
    {
        get => BackingStream.Position;
        set
        {
            if (BackingStream.Position == value)
                return;
            BackingStream.Position = value;
        }
    }

    public override bool Owned => true;

    public override Task CopyToAsync(StreamWrapper destination, CancellationToken cancellationToken = default)
    {
        using var bufferOwner = MemoryPool<byte>.Shared.Rent(81920);
        var buffer = bufferOwner.Memory;
        int bytesRead;
        while ((bytesRead = BackingStream.Read(buffer.Span)) > 0)
        {
            destination.WriteAsync(buffer[..bytesRead], cancellationToken).GetAwaiter().GetResult();
        }
        return destination.Flush(cancellationToken);
    }

    public override void Dispose()
    {
        BackingStream.Dispose();
    }

    public override Task Flush(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public override Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return BackingStream.ReadAsync(buffer, cancellationToken).AsTask();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException("Cannot set length on read-only stream");
    }

    public override Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Cannot write to read-only stream");
    }
}

public sealed record MinioWriteStream(
    MinioClientWrapper Client,
    string ObjectName,
    Stream BackingStream
) : StreamWrapper
{
    public override bool IsReadable => false;

    public override bool IsWritable => BackingStream.CanWrite;

    public override long Length => BackingStream.Length;

    public override long Position
    {
        get => BackingStream.Position;
        set => BackingStream.Position = value;
    }

    public override bool Owned => true;

    public override async Task CopyToAsync(StreamWrapper destination, CancellationToken cancellationToken = default)
    {
        BackingStream.Position = 0;
        using var bufferOwner = MemoryPool<byte>.Shared.Rent(81920);
        var buffer = bufferOwner.Memory;
        int bytesRead;
        while ((bytesRead = BackingStream.Read(buffer.Span)) > 0)
        {
            await destination.WriteAsync(buffer[..bytesRead], cancellationToken).ConfigureAwait(false);
        }
        await destination.Flush(cancellationToken).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        BackingStream.Dispose();
    }

    public override async Task Flush(CancellationToken cancellationToken = default)
    {
        var bpos = BackingStream.Position;
        BackingStream.Position = 0;
        await BackingStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        var mime = FileUtils.InferContentType(ObjectName);
        await Client.Upload(ObjectName, BackingStream, mime).ConfigureAwait(false);
        BackingStream.Position = bpos;
    }

    public override Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Cannot read from write-only stream");
    }

    public override void SetLength(long value)
    {
        BackingStream.SetLength(value);
    }

    public override Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return BackingStream.WriteAsync(buffer, cancellationToken).AsTask();
    }
}
