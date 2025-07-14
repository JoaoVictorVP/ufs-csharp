using System;

namespace ufs;

public readonly record struct FsPath
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

    public FsPath WithRoot(string root)
    {
        if (IsAbsolute)
            return new FsPath(root + Value[1..]);
        if (IsRelative)
            return this;
        throw new InvalidOperationException("Path is in an invalid state.");
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
        basis = basis.Replace("\\", "/");
        if (basis.EndsWith('/') is false)
            basis += '/';
        if (IsAbsolute)
        {
            if (Value.StartsWith(basis))
                return Value;
            return null;
        }
        if (IsRelative)
        {
            if (Value.StartsWith("./"))
                return basis + Value[2..];
            var value = Value;
            while (value.StartsWith("../"))
            {
                basis = Path.GetDirectoryName(basis) ?? throw new InvalidOperationException("Root path cannot be resolved.");
                value = value[3..];
            }
            var final = Path.Combine(basis, value);
            ValidateResolution(final, root);
            return final;
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
