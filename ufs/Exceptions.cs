using System;

namespace ufs;

public class FileSystemException(string message) : Exception(message)
{
    public class Forbidden(string path)
        : FileSystemException($"Access to the path '{path}' is forbidden.");
    public class ReadOnly(string path)
        : FileSystemException($"The file system at '{path}' is read-only.");
}

public class PathException(string message) : FileSystemException(message)
{
    public class ContainsDottedSegmentsInAbsolutePath(string path)
        : PathException($"The absolute path '{path}' contains invalid segments ('.' or '..').");
    public class InvalidPathCharacters(string path)
        : PathException($"The path '{path}' contains invalid characters.");
    public class EmptyPath()
        : PathException("The path cannot be empty.");
    public class NullPath()
        : PathException("The path cannot be null.");
    public class InvalidPath(string path)
        : PathException($"The path '{path}' is invalid or cannot be resolved.");
}
