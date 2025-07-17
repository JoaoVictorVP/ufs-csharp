using System;
using System.Text;

namespace ufs;

public static class FileSystemExtensions
{
    /// <summary>
    /// Writes all text to a file in the filesystem, creating the file if it does not exist.<br/>
    /// If the file exists, it will be overwritten.
    /// If the directory does not exist, it will be created.
    /// If the file system is read-only, it will throw a <see cref="FileSystemException.ReadOnly"/> exception.
    /// </summary>
    /// <param name="fs"></param>
    /// <param name="path"></param>
    /// <param name="content"></param>
    /// <param name="encoding"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public static async Task WriteAllText(this IFileSystem fs, FsPath path, string content, Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        using var file = await fs.CreateFile(path, cancellationToken);
        await file.WriteAllText(content, encoding, cancellationToken);
    }
    /// <summary>
    /// Reads all text from a file in the filesystem.<br/>
    /// If the file does not exist, it throws a <see cref="FileSystemException.NotFound"/> exception.
    /// </summary>
    /// <param name="fs"></param>
    /// <param name="path"></param>
    /// <param name="encoding"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="FileSystemException.NotFound"></exception>
    public static async Task<string> ReadAllText(this IFileSystem fs, FsPath path, Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        using var file = await fs.OpenFileRead(path, cancellationToken)
            ?? throw new FileSystemException.NotFound(path.Value);
        return await file.ReadAllText(encoding, cancellationToken);
    }
}
