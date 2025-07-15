using System;

namespace ufs;

public static class SpanExtensions
{
    public static bool IsEmptyOrWhiteSpace(this ReadOnlySpan<char> span)
        => span.IsEmpty || span.IsWhiteSpace();
}
