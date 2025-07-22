using System;
using System.Diagnostics.CodeAnalysis;

namespace ufs;

public static class StreamExtensions
{
    [return: NotNullIfNotNull(nameof(stream))]
    public static StreamWrapper? Wrap(this Stream? stream)
    {
        if (stream is null)
            return null;
        return new StreamWrapper.Real(stream);
    }

    [return: NotNullIfNotNull(nameof(stream))]
    public static BufferedStream? Buffered(this Stream? stream, int bufferSize = 4096)
    {
        if (stream is null)
            return null;
        return new BufferedStream(stream, bufferSize);
    }
}
