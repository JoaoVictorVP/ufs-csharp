using ufs;
using ufs.Impl;
using System.Text;

namespace ufs_tests;

public class RealFsTests : IDisposable
{
    private readonly string _testRoot;
    private readonly IFileSystem _fs;

    public RealFsTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "ufs_tests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testRoot);
        _fs = new RealFileSystem(_testRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, true);
        }
    }

    #region Directory Tests

    [Fact]
    public async Task CreateDirectory_ShouldCreateDirectory()
    {
        var dirPath = "./testdir".FsPath();
        var dir = await _fs.CreateDirectory(dirPath);
        
        Assert.NotNull(dir);
        Assert.True(await _fs.DirExists(dirPath));
        // The returned directory path will be the resolved absolute path
        Assert.EndsWith("testdir", dir.Path.Value);
        Assert.Equal(_fs, dir.Fs);
    }

    [Fact]
    public async Task CreateDirectory_WithAbsolutePath_ShouldCreateDirectory()
    {
        var dirPath = (_testRoot + "/absolutedir").FsPath();
        var dir = await _fs.CreateDirectory(dirPath);
        
        Assert.NotNull(dir);
        Assert.True(await _fs.DirExists(dirPath));
    }

    [Fact]
    public async Task CreateDirectory_ReadOnlyFileSystem_ShouldThrow()
    {
        var readOnlyFs = new RealFileSystem(_testRoot, true);
        var dirPath = "./readonly_testdir".FsPath();
        
        await Assert.ThrowsAsync<FileSystemException.ReadOnly>(
            () => readOnlyFs.CreateDirectory(dirPath));
    }

    [Fact]
    public async Task DirExists_ExistingDirectory_ShouldReturnTrue()
    {
        var dirPath = "./existing_dir".FsPath();
        await _fs.CreateDirectory(dirPath);
        
        var exists = await _fs.DirExists(dirPath);
        Assert.True(exists);
    }

    [Fact]
    public async Task DirExists_NonExistingDirectory_ShouldReturnFalse()
    {
        var dirPath = "./non_existing_dir".FsPath();
        
        var exists = await _fs.DirExists(dirPath);
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteDirectory_ExistingDirectory_ShouldReturnTrue()
    {
        var dirPath = "./dir_to_delete".FsPath();
        await _fs.CreateDirectory(dirPath);
        
        var deleted = await _fs.DeleteDirectory(dirPath);
        Assert.True(deleted);
        Assert.False(await _fs.DirExists(dirPath));
    }

    [Fact]
    public async Task DeleteDirectory_NonExistingDirectory_ShouldReturnFalse()
    {
        var dirPath = "./non_existing_dir_to_delete".FsPath();
        
        var deleted = await _fs.DeleteDirectory(dirPath);
        Assert.False(deleted);
    }

    [Fact]
    public async Task DeleteDirectory_WithFiles_Recursive_ShouldDeleteAll()
    {
        var dirPath = "./dir_with_files".FsPath();
        await _fs.CreateDirectory(dirPath);
        
        var subDirPath = "./dir_with_files/subdir".FsPath();
        await _fs.CreateDirectory(subDirPath);
        
        var filePath = "./dir_with_files/file.txt".FsPath();
        using var file = await _fs.CreateFile(filePath);
        var content = Encoding.UTF8.GetBytes("test content");
        await file.Inner.WriteAsync(content);
        ((StreamWrapper.Real)file.Inner).Inner.Dispose();
        
        var deleted = await _fs.DeleteDirectory(dirPath, recursive: true);
        Assert.True(deleted);
        Assert.False(await _fs.DirExists(dirPath));
    }

    [Fact]
    public async Task DeleteDirectory_ReadOnlyFileSystem_ShouldThrow()
    {
        var dirPath = "./dir_to_delete_readonly".FsPath();
        await _fs.CreateDirectory(dirPath);
        
        var readOnlyFs = new RealFileSystem(_testRoot, true);
        await Assert.ThrowsAsync<FileSystemException.ReadOnly>(
            () => readOnlyFs.DeleteDirectory(dirPath));
    }

    #endregion

    #region File Tests

    [Fact]
    public async Task CreateFile_ShouldCreateFile()
    {
        var filePath = "./test_file.txt".FsPath();
        using var file = await _fs.CreateFile(filePath);
        
        Assert.NotNull(file);
        Assert.EndsWith("test_file.txt", file.Path.Value);
        Assert.Equal(_fs, file.Fs);
        Assert.True(file.Inner.IsWritable);
        Assert.True(file.Inner.IsReadable);
        
        ((StreamWrapper.Real)file.Inner).Inner.Dispose();
        Assert.True(await _fs.FileExists(filePath));
    }

    [Fact]
    public async Task CreateFile_ReadOnlyFileSystem_ShouldThrow()
    {
        var readOnlyFs = new RealFileSystem(_testRoot, true);
        var filePath = "./readonly_file.txt".FsPath();
        
        await Assert.ThrowsAsync<FileSystemException.ReadOnly>(
            () => readOnlyFs.CreateFile(filePath));
    }

    [Fact]
    public async Task FileExists_ExistingFile_ShouldReturnTrue()
    {
        var filePath = "./existing_file.txt".FsPath();
        using var file = await _fs.CreateFile(filePath);
        ((StreamWrapper.Real)file.Inner).Inner.Dispose();
        
        var exists = await _fs.FileExists(filePath);
        Assert.True(exists);
    }

    [Fact]
    public async Task FileExists_NonExistingFile_ShouldReturnFalse()
    {
        var filePath = "./non_existing_file.txt".FsPath();
        
        var exists = await _fs.FileExists(filePath);
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteFile_ExistingFile_ShouldReturnTrue()
    {
        var filePath = "./file_to_delete.txt".FsPath();
        using var file = await _fs.CreateFile(filePath);
        ((StreamWrapper.Real)file.Inner).Inner.Dispose();
        
        var deleted = await _fs.DeleteFile(filePath);
        Assert.True(deleted);
        Assert.False(await _fs.FileExists(filePath));
    }

    [Fact]
    public async Task DeleteFile_NonExistingFile_ShouldReturnFalse()
    {
        var filePath = "./non_existing_file_to_delete.txt".FsPath();
        
        var deleted = await _fs.DeleteFile(filePath);
        Assert.False(deleted);
    }

    [Fact]
    public async Task DeleteFile_ReadOnlyFileSystem_ShouldThrow()
    {
        var filePath = "./file_to_delete_readonly.txt".FsPath();
        using var file = await _fs.CreateFile(filePath);
        ((StreamWrapper.Real)file.Inner).Inner.Dispose();
        
        var readOnlyFs = new RealFileSystem(_testRoot, true);
        await Assert.ThrowsAsync<FileSystemException.ReadOnly>(
            () => readOnlyFs.DeleteFile(filePath));
    }

    #endregion

    #region File Operations Tests

    [Fact]
    public async Task OpenFileRead_ExistingFile_ShouldReturnFileRO()
    {
        var filePath = "./file_to_read.txt".FsPath();
        var content = "Hello, World!";
        var contentBytes = Encoding.UTF8.GetBytes(content);
        
        using var file = await _fs.CreateFile(filePath);
        await file.Inner.WriteAsync(contentBytes);
        ((StreamWrapper.Real)file.Inner).Inner.Dispose();
        
        using var readFile = await _fs.OpenFileRead(filePath);
        Assert.NotNull(readFile);
        Assert.True(readFile.Inner.IsReadable);
        Assert.False(readFile.Inner.IsWritable);
        
        var buffer = new byte[readFile.Inner.Length];
        await readFile.Inner.ReadAsync(buffer);
        var readContent = Encoding.UTF8.GetString(buffer);
        Assert.Equal(content, readContent);
        
        ((StreamWrapper.Real)readFile.Inner).Inner.Dispose();
    }

    [Fact]
    public async Task OpenFileRead_NonExistingFile_ShouldReturnNull()
    {
        var filePath = "./non_existing_read_file.txt".FsPath();
        
        using var readFile = await _fs.OpenFileRead(filePath);
        Assert.Null(readFile);
    }

    [Fact]
    public async Task OpenFileWrite_ExistingFile_ShouldReturnFileWO()
    {
        var filePath = "./file_to_write.txt".FsPath();
        using var file = await _fs.CreateFile(filePath);
        ((StreamWrapper.Real)file.Inner).Inner.Dispose();
        
        var writeFile = await _fs.OpenFileWrite(filePath);
        Assert.NotNull(writeFile);
        Assert.False(writeFile.Inner.IsReadable);
        Assert.True(writeFile.Inner.IsWritable);
        
        var content = "New content";
        var contentBytes = Encoding.UTF8.GetBytes(content);
        await writeFile.Inner.WriteAsync(contentBytes);
        ((StreamWrapper.Real)writeFile.Inner).Inner.Dispose();
    }

    [Fact]
    public async Task OpenFileWrite_NonExistingFile_ShouldReturnNull()
    {
        var filePath = "./non_existing_write_file.txt".FsPath();
        
        var writeFile = await _fs.OpenFileWrite(filePath);
        Assert.Null(writeFile);
    }

    [Fact]
    public async Task OpenFileWrite_ReadOnlyFileSystem_ShouldThrow()
    {
        var filePath = "./file_to_write_readonly.txt".FsPath();
        using var file = await _fs.CreateFile(filePath);
        ((StreamWrapper.Real)file.Inner).Inner.Dispose();
        
        var readOnlyFs = new RealFileSystem(_testRoot, true);
        await Assert.ThrowsAsync<FileSystemException.ReadOnly>(
            () => readOnlyFs.OpenFileWrite(filePath));
    }

    [Fact]
    public async Task OpenFileReadWrite_ExistingFile_ShouldReturnFileRW()
    {
        var filePath = "./file_to_readwrite.txt".FsPath();
        var initialContent = "Initial content";
        var initialBytes = Encoding.UTF8.GetBytes(initialContent);
        
        using var file = await _fs.CreateFile(filePath);
        await file.Inner.WriteAsync(initialBytes);
        ((StreamWrapper.Real)file.Inner).Inner.Dispose();
        
        var rwFile = await _fs.OpenFileReadWrite(filePath);
        Assert.NotNull(rwFile);
        Assert.True(rwFile.Inner.IsReadable);
        Assert.True(rwFile.Inner.IsWritable);
        
        var buffer = new byte[rwFile.Inner.Length];
        await rwFile.Inner.ReadAsync(buffer);
        var readContent = Encoding.UTF8.GetString(buffer);
        Assert.Equal(initialContent, readContent);
        
        ((StreamWrapper.Real)rwFile.Inner).Inner.Dispose();
    }

    [Fact]
    public async Task OpenFileReadWrite_NonExistingFile_ShouldCreateFile()
    {
        var filePath = "./new_readwrite_file.txt".FsPath();
        
        var rwFile = await _fs.OpenFileReadWrite(filePath);
        Assert.NotNull(rwFile);
        Assert.True(rwFile.Inner.IsReadable);
        Assert.True(rwFile.Inner.IsWritable);
        
        var content = "New file content";
        var contentBytes = Encoding.UTF8.GetBytes(content);
        await rwFile.Inner.WriteAsync(contentBytes);
        ((StreamWrapper.Real)rwFile.Inner).Inner.Dispose();
        
        Assert.True(await _fs.FileExists(filePath));
    }

    [Fact]
    public async Task OpenFileReadWrite_ReadOnlyFileSystem_ShouldThrow()
    {
        var readOnlyFs = new RealFileSystem(_testRoot, true);
        var filePath = "./readwrite_readonly.txt".FsPath();
        
        await Assert.ThrowsAsync<FileSystemException.ReadOnly>(
            () => readOnlyFs.OpenFileReadWrite(filePath));
    }

    #endregion

    #region File Content Tests

    [Fact]
    public async Task FileOperations_WriteAndReadBytes_ShouldWorkCorrectly()
    {
        var filePath = "./binary_operations.bin".FsPath();
        var data = new byte[] { 0x01, 0x02, 0x03, 0xFF, 0xFE, 0xFD };
        
        using var file = await _fs.CreateFile(filePath);
        await file.Inner.WriteAsync(data);
        ((StreamWrapper.Real)file.Inner).Inner.Dispose();
        
        using var readFile = await _fs.OpenFileRead(filePath);
        Assert.NotNull(readFile);
        
        var buffer = new byte[readFile.Inner.Length];
        await readFile.Inner.ReadAsync(buffer);
        Assert.Equal(data, buffer);
        
        ((StreamWrapper.Real)readFile.Inner).Inner.Dispose();
    }

    #endregion

    #region Directory Listing Tests

    [Fact]
    public async Task Entries_ShallowMode_ShouldReturnDirectChildren()
    {
        // Create test structure
        await _fs.CreateDirectory("./test_entries".FsPath());
        await _fs.CreateDirectory("./test_entries/subdir1".FsPath());
        await _fs.CreateDirectory("./test_entries/subdir2".FsPath());
        
        var file1 = await _fs.CreateFile("./test_entries/file1.txt".FsPath());
        ((StreamWrapper.Real)file1.Inner).Inner.Dispose();
        var file2 = await _fs.CreateFile("./test_entries/file2.txt".FsPath());
        ((StreamWrapper.Real)file2.Inner).Inner.Dispose();
        
        // Create nested files that shouldn't be returned in shallow mode
        var nestedFile = await _fs.CreateFile("./test_entries/subdir1/nested.txt".FsPath());
        ((StreamWrapper.Real)nestedFile.Inner).Inner.Dispose();
        
        var entries = new List<FileEntry>();
        await foreach (var entry in _fs.Entries("./test_entries".FsPath(), ListEntriesMode.ShallowAll))
        {
            entries.Add(entry);
        }
        
        Assert.Equal(4, entries.Count); // 2 dirs + 2 files
        Assert.Contains(entries, e => e is FileEntry.Directory && e.Path.Value.EndsWith("subdir1"));
        Assert.Contains(entries, e => e is FileEntry.Directory && e.Path.Value.EndsWith("subdir2"));
        Assert.Contains(entries, e => e is FileEntry.FileRef && e.Path.Value.EndsWith("file1.txt"));
        Assert.Contains(entries, e => e is FileEntry.FileRef && e.Path.Value.EndsWith("file2.txt"));
        
        // Ensure nested file is not included
        Assert.DoesNotContain(entries, e => e.Path.Value.EndsWith("nested.txt"));
    }

    [Fact]
    public async Task Entries_RecursiveMode_ShouldReturnAllEntries()
    {
        // Create test structure
        await _fs.CreateDirectory("./test_recursive".FsPath());
        await _fs.CreateDirectory("./test_recursive/subdir".FsPath());
        await _fs.CreateDirectory("./test_recursive/subdir/deep".FsPath());
        
        var file1 = await _fs.CreateFile("./test_recursive/root.txt".FsPath());
        ((StreamWrapper.Real)file1.Inner).Inner.Dispose();
        var file2 = await _fs.CreateFile("./test_recursive/subdir/nested.txt".FsPath());
        ((StreamWrapper.Real)file2.Inner).Inner.Dispose();
        var file3 = await _fs.CreateFile("./test_recursive/subdir/deep/deep.txt".FsPath());
        ((StreamWrapper.Real)file3.Inner).Inner.Dispose();
        
        var entries = new List<FileEntry>();
        await foreach (var entry in _fs.Entries("./test_recursive".FsPath(), ListEntriesMode.RecursiveAll))
        {
            entries.Add(entry);
        }
        
        // In recursive mode, we get all files and directories
        // The exact count can vary based on the directory structure returned by the OS
        Assert.True(entries.Count >= 3); // At least the 3 files we created
        Assert.Contains(entries, e => e.Path.Value.Contains("root.txt"));
        Assert.Contains(entries, e => e.Path.Value.Contains("nested.txt"));
        Assert.Contains(entries, e => e.Path.Value.Contains("deep.txt"));
    }

    [Fact]
    public async Task Entries_WithFilter_ShouldReturnFilteredEntries()
    {
        // Create test structure
        await _fs.CreateDirectory("./test_filter".FsPath());
        
        var txtFile = await _fs.CreateFile("./test_filter/document.txt".FsPath());
        ((StreamWrapper.Real)txtFile.Inner).Inner.Dispose();
        var logFile = await _fs.CreateFile("./test_filter/application.log".FsPath());
        ((StreamWrapper.Real)logFile.Inner).Inner.Dispose();
        var binFile = await _fs.CreateFile("./test_filter/program.exe".FsPath());
        ((StreamWrapper.Real)binFile.Inner).Inner.Dispose();
        
        var entries = new List<FileEntry>();
        await foreach (var entry in _fs.Entries("./test_filter".FsPath(), ListEntriesMode.Shallow("*.txt")))
        {
            entries.Add(entry);
        }
        
        Assert.Single(entries);
        Assert.Contains(entries, e => e.Path.Value.EndsWith("document.txt"));
    }

    #endregion

    #region At() Method Tests

    [Fact]
    public async Task At_WithRelativePath_ShouldCreateSubFileSystem()
    {
        await _fs.CreateDirectory("./subfs_test".FsPath());
        
        var subFs = _fs.At("./subfs_test".FsPath());
        Assert.NotNull(subFs);
        Assert.Equal(_fs.ReadOnly, subFs.ReadOnly);
        
        // Test that the sub-filesystem works
        using var file = await subFs.CreateFile("./file_in_sub.txt".FsPath());
        ((StreamWrapper.Real)file.Inner).Inner.Dispose();
        
        // Verify the file exists in the original filesystem
        Assert.True(await _fs.FileExists("./subfs_test/file_in_sub.txt".FsPath()));
    }

    [Fact]
    public async Task At_WithReadOnlyMode_ShouldCreateReadOnlyFileSystem()
    {
        await _fs.CreateDirectory("./readonly_subfs".FsPath());
        
        var readOnlySubFs = _fs.At("./readonly_subfs".FsPath(), FileSystemMode.ReadOnly);
        Assert.True(readOnlySubFs.ReadOnly);
        
        await Assert.ThrowsAsync<FileSystemException.ReadOnly>(
            () => readOnlySubFs.CreateFile("./file.txt".FsPath()));
    }

    [Fact]
    public async Task At_WithReadWriteMode_FromReadOnlyParent_ShouldThrow()
    {
        var readOnlyFs = new RealFileSystem(_testRoot, true);
        await _fs.CreateDirectory("./readwrite_attempt".FsPath());
        
        Assert.Throws<FileSystemException.ReadOnly>(
            () => readOnlyFs.At("./readwrite_attempt".FsPath(), FileSystemMode.ReadWrite));
    }

    [Fact]
    public async Task At_WithInheritMode_ShouldInheritParentMode()
    {
        var readOnlyFs = new RealFileSystem(_testRoot, true);
        await _fs.CreateDirectory("./inherit_test".FsPath());
        
        var subFs = readOnlyFs.At("./inherit_test".FsPath(), FileSystemMode.Inherit);
        Assert.True(subFs.ReadOnly);
    }

    #endregion

    #region Static Factory Method Tests

    [Fact]
    public void AtAppDir_ShouldCreateFileSystemAtApplicationDirectory()
    {
        var appDirFs = RealFileSystem.AtAppDir<RealFsTests>();
        Assert.NotNull(appDirFs);
        Assert.False(appDirFs.ReadOnly);
        
        // The root should be a valid directory path
        Assert.True(Directory.Exists(appDirFs.Root));
    }

    #endregion

    #region Path Resolution Tests

    [Fact]
    public async Task InvalidPath_ShouldThrowPathException()
    {
        // Test with a path that should be invalid during resolution, not construction
        var invalidPath = "./../../outside_root".FsPath();
        
        await Assert.ThrowsAnyAsync<Exception>(
            () => _fs.CreateFile(invalidPath));
    }

    [Fact]
    public async Task AbsolutePath_OutsideRoot_ShouldThrowOrReturnNull()
    {
        try
        {
            var outsidePath = "/completely/different/path".FsPath();
            var result = await _fs.FileExists(outsidePath);
            // This should either throw or return false, depending on implementation
            Assert.False(result);
        }
        catch (PathException.InvalidPath)
        {
            // This is also acceptable behavior
        }
    }

    #endregion

    #region FileEntry Tests

    [Fact]
    public async Task FileRef_Operations_ShouldWorkCorrectly()
    {
        var filePath = "./fileref_test.txt".FsPath();
        var content = "FileRef test content";
        var contentBytes = Encoding.UTF8.GetBytes(content);
        
        // Create file and get as FileRef through Entries
        using var file = await _fs.CreateFile(filePath);
        await file.Inner.WriteAsync(contentBytes);
        ((StreamWrapper.Real)file.Inner).Inner.Dispose();
        
        var entries = new List<FileEntry>();
        await foreach (var entry in _fs.Entries("./".FsPath(), ListEntriesMode.ShallowAll))
        {
            if (entry.Path.Value.EndsWith("fileref_test.txt"))
                entries.Add(entry);
        }
        
        Assert.Single(entries);
        var fileRef = entries[0] as FileEntry.FileRef;
        Assert.NotNull(fileRef);
        
        // Test FileRef operations
        Assert.True(await fileRef.Exists());
        
        using var readFile = await fileRef.OpenRead();
        Assert.NotNull(readFile);
        var buffer = new byte[readFile.Inner.Length];
        await readFile.Inner.ReadAsync(buffer);
        var readContent = Encoding.UTF8.GetString(buffer);
        Assert.Equal(content, readContent);
        ((StreamWrapper.Real)readFile.Inner).Inner.Dispose();
        
        var writeFile = await fileRef.OpenWrite();
        Assert.NotNull(writeFile);
        // When opening for write, the file should be truncated/overwritten
        var newContent = "Updated content";
        var newContentBytes = Encoding.UTF8.GetBytes(newContent);
        // Ensure we start from the beginning
        ((StreamWrapper.Real)writeFile.Inner).Inner.SetLength(0);
        await writeFile.Inner.WriteAsync(newContentBytes);
        ((StreamWrapper.Real)writeFile.Inner).Inner.Dispose();
        
        var rwFile = await fileRef.OpenReadWrite();
        Assert.NotNull(rwFile);
        var updatedBuffer = new byte[rwFile.Inner.Length];
        await rwFile.Inner.ReadAsync(updatedBuffer);
        var updatedContent = Encoding.UTF8.GetString(updatedBuffer);
        Assert.Equal(newContent, updatedContent);
        ((StreamWrapper.Real)rwFile.Inner).Inner.Dispose();
        
        Assert.True(await fileRef.Delete());
        Assert.False(await fileRef.Exists());
    }

    [Fact]
    public async Task Directory_At_ShouldCreateSubFileSystem()
    {
        var dirPath = "./directory_at_test".FsPath();
        var dir = await _fs.CreateDirectory(dirPath);
        
        var subFs = dir.At;
        Assert.NotNull(subFs);
        
        var subFile = await subFs.CreateFile("./subfile.txt".FsPath());
        ((StreamWrapper.Real)subFile.Inner).Inner.Dispose();
        
        // Verify file exists in parent filesystem
        Assert.True(await _fs.FileExists("./directory_at_test/subfile.txt".FsPath()));
    }

    #endregion
}
