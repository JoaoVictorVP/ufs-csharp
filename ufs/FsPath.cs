using System;
using System.Diagnostics.CodeAnalysis;

namespace ufs;

public readonly struct FsPath
{
    static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();

    public string Value { get; init; }

    public ReadOnlySpan<char> Extension => Path.GetExtension(Value.AsSpan());
    public ReadOnlySpan<char> FileName => Path.GetFileName(Value.AsSpan());
    public ReadOnlySpan<char> FileNameWithoutExtension => Path.GetFileNameWithoutExtension(Value.AsSpan());
    public ReadOnlySpan<char> DirectoryName => Path.GetDirectoryName(Value.AsSpan());
    public bool IsAbsolute => Value.StartsWith('/');
    public bool IsRelative => !IsAbsolute;
    public bool IsRoot => Value == "/";

    public IEnumerable<ReadOnlyMemory<char>> Segments(FsPath workingDirectory)
    {
        var self = this;
        if (self.IsRelative)
            self = self.Locate(workingDirectory);
        var value = self.Value.AsMemory();
        var slashCount = value.Span.Count('/');
        // Span<Range> segments = stackalloc Range[slashCount + 1];
        Range[] segments = new Range[slashCount + 1];
        int segmentCount = value.Span.Split(segments, '/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segmentRange in segments[..segmentCount])
        {
            var segment = value[segmentRange];
            yield return segment;
        }
    }

    public FsPath Locate(FsPath workingDirectory)
    {
        if (IsAbsolute)
            return this;
        var final = Path.GetFullPath(Value, workingDirectory.Value);
        return final.FsPath();
    }

    public FsPath DirectoryPath =>
        DirectoryName is ""
            ? this
            : new(Path.GetDirectoryName(Value)
                ?? throw new InvalidOperationException("Path is in an invalid state."));

    public FsPath WithRoot(string root)
    {
        if (IsAbsolute)
            return new FsPath(root + Value[1..]);
        if (IsRelative)
            return this;
        throw new InvalidOperationException("Path is in an invalid state.");
    }

    public FsPath Appending(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            throw new ArgumentException("Segment cannot be null or whitespace.", nameof(segment));
        if (IsRoot)
            return new FsPath(Value + segment);
        if (IsAbsolute)
            return new FsPath(Value + '/' + segment);
        return new FsPath(Value + '/' + segment);
    }

    static void ValidateResolution(string path, string root)
    {
        if (path.StartsWith(root) is false)
            throw new FileSystemException.Forbidden(path);
    }

    /// <summary>
    /// Resolves the path against the given base path.<br/>
    /// If the path is absolute, it checks if it starts with the base path.<br/>
    /// If the path is relative, it resolves it against the base path.<br/>
    /// If the path is not resolvable, it returns null.<br/>
    /// If the path is in an invalid state, it throws an <see cref="InvalidOperationException"/>.<br/>
    /// </summary>
    /// <param name="basis"></param>
    /// <param name="root"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="FileSystemException.Forbidden"></exception>
    public string? Resolve(string basis, string root)
    {
        if (Value is "." or "./")
            return basis;

        basis = basis.Replace("\\", "/");
        if (basis.EndsWith('/') is false)
            basis += '/';
        if (IsAbsolute)
        {
            if (Value.StartsWith(basis[..1]))
                return Value;
            return null;
        }
        if (IsRelative)
        {
            var resolved = Path.GetFullPath(Value, basis);
            ValidateResolution(resolved, root);
            return resolved;
        }
        throw new InvalidOperationException("Path is in an invalid state.");
    }

    /// <summary>
    /// <paramref name="value"/> specs:<br/>
    /// * should not be null (throws <see cref="PathException.NullPath"/>)<br/>
    /// * should not be empty (throws <see cref="PathException.EmptyPath"/>)<br/>
    /// * if relative, should start with "./" or "../"<br/>
    /// * if absolute, should start with "/" (root)<br/>
    /// * if absolute, should not contain ".." or "." segments (throws <see cref="PathException.NullPath"/>)<br/>
    /// * should not contain any invalid characters for file paths (throws <see cref="PathException.InvalidPathCharacters"/>)<br/>
    /// </summary>
    /// <param name="value"></param>
    /// <exception cref="PathException.NullPath">Thrown when the path is null.</exception>
    /// <exception cref="PathException.EmptyPath">Thrown when the path is empty.</exception>
    /// <exception cref="PathException.ContainsDottedSegmentsInAbsolutePath">Thrown when the absolute path contains invalid segments ('.' or '..').</exception>
    /// <exception cref="PathException.InvalidPathCharacters">Thrown when the path contains invalid characters.</exception>
    public FsPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new PathException.EmptyPath();

        if (value is ".")
        {
            Value = value;
            return;
        }

        if (value.StartsWith('/'))
        {
            var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment => segment == "." || segment == ".."))
                throw new PathException.ContainsDottedSegmentsInAbsolutePath(value);
        }

        foreach (var invalidChar in InvalidPathChars)
            if (value.Contains(invalidChar))
                throw new PathException.InvalidPathCharacters(value);
        Value = value?.Replace("\\", "/") ?? throw new PathException.NullPath();
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => obj switch
    {
        FsPath other => Equals(other),
        _ => false
    };
    public bool Equals(FsPath other)
    {
        var left = this;
        var right = other;

        bool rawEq = left.Value == right.Value;
        if (rawEq)
            return true;
        var (lhsAbs, rhs) = (left.IsAbsolute, right.IsAbsolute) switch
        {
            (true, _) => (left, right),
            (_, true) => (right, left),
            _ => (left, right)
        };
        var rhsLocated = rhs.Locate(lhsAbs.DirectoryPath);
        return lhsAbs.Value == rhsLocated.Value;
    }
    public override int GetHashCode()
        => Value.GetHashCode();
    public override string ToString()
        => Value;

    public static bool operator ==(FsPath left, FsPath right)
        => left.Equals(right);
    public static bool operator !=(FsPath left, FsPath right)
        => left.Equals(right) is false;
}
public static class FsPathExtensions
{
    public static FsPath FsPath(this string value)
    {
        return new FsPath(value);
    }

    public static FsPath FsPath(this ReadOnlySpan<char> value)
    {
        return new FsPath(value.ToString());
    }

    public static FsPath FsPath(this ReadOnlyMemory<char> value)
    {
        return new FsPath(value.Span.ToString());
    }
}
