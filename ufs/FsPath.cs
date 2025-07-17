using System;
using System.Diagnostics.CodeAnalysis;

namespace ufs;

public readonly record struct FsPath
{
    static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();

    public static readonly FsPath Root = new("/");

    public string Value { get; init; }

    public ReadOnlySpan<char> Extension => Path.GetExtension(Value.AsSpan());
    public ReadOnlySpan<char> FileName => Path.GetFileName(Value.AsSpan());
    public ReadOnlySpan<char> FileNameWithoutExtension => Path.GetFileNameWithoutExtension(Value.AsSpan());
    public ReadOnlySpan<char> DirectoryName => Path.GetDirectoryName(Value.AsSpan());
    public bool IsRoot => Value == "/";

    public IEnumerable<ReadOnlyMemory<char>> Segments(FsPath workingDirectory)
    {
        var value = Value.AsMemory();
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

    public FsPath DirectoryPath =>
        DirectoryName is ""
            ? this
            : new(Path.GetDirectoryName(Value)
                ?? throw new InvalidOperationException("Path is in an invalid state."));

    public FsPath ChangeDirectory(FsPath oldDir, FsPath newDir)
    {
        if (oldDir == newDir)
            return this;

        if (Value.StartsWith(oldDir.Value))
        {
            string newPath;
            if(newDir.IsRoot)
                newPath = Value[oldDir.Value.Length..];
            else if (newDir.Value.EndsWith('/'))
                newPath = newDir.Value + Value[oldDir.Value.Length..];
            else
                newPath = newDir.Value + '/' + Value[oldDir.Value.Length..];
            return new FsPath(newPath);
        }

        throw new InvalidOperationException($"Cannot change directory from {oldDir} to {newDir} for path {this}.");
    }

    public bool InDirectory(FsPath directory)
    {
        var curDir = DirectoryPath;
        var lastDir = "/".FsPath();
        while (curDir != lastDir)
        {
            if (curDir == directory)
                return true;
            curDir = curDir.DirectoryPath;
        }
        return curDir == directory;
    }

    public FsPath Appending(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            throw new ArgumentException("Segment cannot be null or whitespace.", nameof(segment));
        foreach(var c in segment)
        {
            if (InvalidPathChars.Contains(c))
                throw new PathException.InvalidPathCharacters(Value);
        }
        return new FsPath(Path.Combine(Value, segment));
    }

    public string FullPath(string root)
    {
        if (Value.StartsWith(root))
            return Value;
        return Path.GetFullPath(Value[1..], root);
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
        if (value.Contains('\\'))
            value = value.Replace('\\', '/');
        if (value.StartsWith('/') is false)
            throw new PathException.InvalidPath(value);
        // if(value.EndsWith('/'))
        //     value = value.TrimEnd('/');

        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == "." || segment == ".."))
            throw new PathException.ContainsDottedSegmentsInAbsolutePath(value);

        foreach (var invalidChar in InvalidPathChars)
            if (value.Contains(invalidChar))
                throw new PathException.InvalidPathCharacters(value);
        Value = value ?? throw new PathException.NullPath();
    }

    public override string ToString()
        => Value;

    public static FsPath operator /(FsPath path, string segment)
        => path.Appending(segment);
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
