using System;
using System.Text;
using ufs;
using ufs.Impl;
using ufs.Impl.InMemory;

namespace ufs_tests;

public class MemoryFsTests : IDisposable
{
    private readonly string testRoot = "/tmp/ufs_test_root";
    private readonly IFileSystem fs;

    public MemoryFsTests()
    {
        var root = new MemoryFileTree.Root();
        var tmp = root.CreateDir("tmp");
        var testRootDir = tmp.CreateDir("ufs_test_root");
        fs = new MemoryFileSystem(testRootDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, true);
        }
    }

    #region Directory Tests

    [Fact]
    public async Task CreateDirectory_ShouldCreateDirectory()
    {
        var dirPath = "/testdir".FsPath();
        var dir = await fs.CreateDirectory(dirPath);
        
        Assert.NotNull(dir);
        Assert.True(await fs.DirExists(dirPath));
        // The returned directory path will be the resolved absolute path
        Assert.EndsWith("testdir", dir.Path.Value);
        Assert.Equal(fs, dir.Fs);
    }

    [Fact]
    public async Task CreateDirectory_WithAbsolutePath_ShouldCreateDirectory()
    {
        var dirPath = (testRoot + "/absolutedir").FsPath();
        var dir = await fs.CreateDirectory(dirPath);
        
        Assert.NotNull(dir);
        Assert.True(await fs.DirExists(dirPath));
    }

    [Fact]
    public async Task CreateDirectory_ReadOnlyFileSystem_ShouldThrow()
    {
        var readOnlyFs = new RealFileSystem(testRoot, true);
        var dirPath = "/readonly_testdir".FsPath();
        
        await Assert.ThrowsAsync<FileSystemException.ReadOnly>(
            () => readOnlyFs.CreateDirectory(dirPath));
    }

    [Fact]
    public async Task DirExists_ExistingDirectory_ShouldReturnTrue()
    {
        var dirPath = "/existing_dir".FsPath();
        await fs.CreateDirectory(dirPath);
        
        var exists = await fs.DirExists(dirPath);
        Assert.True(exists);
    }

    [Fact]
    public async Task DirExists_NonExistingDirectory_ShouldReturnFalse()
    {
        var dirPath = "/non_existing_dir".FsPath();
        
        var exists = await fs.DirExists(dirPath);
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteDirectory_ExistingDirectory_ShouldReturnTrue()
    {
        var dirPath = "/dir_to_delete".FsPath();
        await fs.CreateDirectory(dirPath);
        
        var deleted = await fs.DeleteDirectory(dirPath);
        Assert.True(deleted);
        Assert.False(await fs.DirExists(dirPath));
    }

    [Fact]
    public async Task DeleteDirectory_NonExistingDirectory_ShouldReturnFalse()
    {
        var dirPath = "/non_existing_dir_to_delete".FsPath();
        
        var deleted = await fs.DeleteDirectory(dirPath);
        Assert.False(deleted);
    }

    [Fact]
    public async Task DeleteDirectory_WithFiles_Recursive_ShouldDeleteAll()
    {
        var dirPath = "/dir_with_files".FsPath();
        await fs.CreateDirectory(dirPath);
        
        var subDirPath = "/dir_with_files/subdir".FsPath();
        await fs.CreateDirectory(subDirPath);
        
        var filePath = "/dir_with_files/file.txt".FsPath();
        using var file = await fs.CreateFile(filePath);
        var content = Encoding.UTF8.GetBytes("test content");
        await file.Inner.WriteAsync(content);
        file.Inner.Dispose();
        
        var deleted = await fs.DeleteDirectory(dirPath, recursive: true);
        Assert.True(deleted);
        Assert.False(await fs.DirExists(dirPath));
    }

    [Fact]
    public async Task DeleteDirectory_ReadOnlyFileSystem_ShouldThrow()
    {
        var dirPath = "/dir_to_delete_readonly".FsPath();
        await fs.CreateDirectory(dirPath);
        
        var readOnlyFs = new RealFileSystem(testRoot, true);
        await Assert.ThrowsAsync<FileSystemException.ReadOnly>(
            () => readOnlyFs.DeleteDirectory(dirPath));
    }

    #endregion

    #region File Tests

    [Fact]
    public async Task CreateFile_ShouldCreateFile()
    {
        var filePath = "/test_file.txt".FsPath();
        using var file = await fs.CreateFile(filePath);
        
        Assert.NotNull(file);
        Assert.EndsWith("test_file.txt", file.Path.Value);
        Assert.Equal(fs, file.Fs);
        Assert.True(file.Inner.IsWritable);
        Assert.True(file.Inner.IsReadable);
        
        file.Inner.Dispose();
        Assert.True(await fs.FileExists(filePath));
    }

    [Fact]
    public async Task CreateFile_ReadOnlyFileSystem_ShouldThrow()
    {
        var readOnlyFs = new RealFileSystem(testRoot, true);
        var filePath = "/readonly_file.txt".FsPath();
        
        await Assert.ThrowsAsync<FileSystemException.ReadOnly>(
            () => readOnlyFs.CreateFile(filePath));
    }

    [Fact]
    public async Task FileExists_ExistingFile_ShouldReturnTrue()
    {
        var filePath = "/existing_file.txt".FsPath();
        using var file = await fs.CreateFile(filePath);
        file.Inner.Dispose();
        
        var exists = await fs.FileExists(filePath);
        Assert.True(exists);
    }

    [Fact]
    public async Task FileExists_NonExistingFile_ShouldReturnFalse()
    {
        var filePath = "/non_existing_file.txt".FsPath();
        
        var exists = await fs.FileExists(filePath);
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteFile_ExistingFile_ShouldReturnTrue()
    {
        var filePath = "/file_to_delete.txt".FsPath();
        var file = await fs.CreateFile(filePath);
        file.Inner.Dispose();
        
        var deleted = await fs.DeleteFile(filePath);
        Assert.True(deleted);
        Assert.False(await fs.FileExists(filePath));
    }

    [Fact]
    public async Task DeleteFile_NonExistingFile_ShouldReturnFalse()
    {
        var filePath = "/non_existing_file_to_delete.txt".FsPath();
        
        var deleted = await fs.DeleteFile(filePath);
        Assert.False(deleted);
    }

    [Fact]
    public async Task DeleteFile_ReadOnlyFileSystem_ShouldThrow()
    {
        var filePath = "/file_to_delete_readonly.txt".FsPath();
        using var file = await fs.CreateFile(filePath);
        file.Inner.Dispose();
        
        var readOnlyFs = new RealFileSystem(testRoot, true);
        await Assert.ThrowsAsync<FileSystemException.ReadOnly>(
            () => readOnlyFs.DeleteFile(filePath));
    }

    #endregion

    #region File Operations Tests

    [Fact]
    public async Task OpenFileRead_ExistingFile_ShouldReturnFileRO()
    {
        var filePath = "/file_to_read.txt".FsPath();
        var content = "Hello, World!";
        var contentBytes = Encoding.UTF8.GetBytes(content);
        
        using var file = await fs.CreateFile(filePath);
        await file.Inner.WriteAsync(contentBytes);
        file.Inner.Dispose();
        
        using var readFile = await fs.OpenFileRead(filePath);
        Assert.NotNull(readFile);
        Assert.True(readFile.Inner.IsReadable);
        Assert.False(readFile.Inner.IsWritable);
        
        var buffer = new byte[readFile.Inner.Length];
        await readFile.Inner.ReadAsync(buffer);
        var readContent = Encoding.UTF8.GetString(buffer);
        Assert.Equal(content, readContent);
        
        readFile.Inner.Dispose();
    }

    [Fact]
    public async Task OpenFileRead_NonExistingFile_ShouldReturnNull()
    {
        var filePath = "/non_existing_read_file.txt".FsPath();
        
        using var readFile = await fs.OpenFileRead(filePath);
        Assert.Null(readFile);
    }

    [Fact]
    public async Task OpenFileWrite_ExistingFile_ShouldReturnFileWO()
    {
        var filePath = "/file_to_write.txt".FsPath();
        using var file = await fs.CreateFile(filePath);
        file.Inner.Dispose();
        
        var writeFile = await fs.OpenFileWrite(filePath);
        Assert.NotNull(writeFile);
        Assert.False(writeFile.Inner.IsReadable);
        Assert.True(writeFile.Inner.IsWritable);
        
        var content = "New content";
        var contentBytes = Encoding.UTF8.GetBytes(content);
        await writeFile.Inner.WriteAsync(contentBytes);
        writeFile.Inner.Dispose();
    }

    [Fact]
    public async Task OpenFileWrite_NonExistingFile_ShouldReturnNotNull()
    {
        var filePath = "/non_existing_write_file.txt".FsPath();
        
        var writeFile = await fs.OpenFileWrite(filePath);
        Assert.NotNull(writeFile);
    }

    [Fact]
    public async Task OpenFileWrite_ReadOnlyFileSystem_ShouldThrow()
    {
        var filePath = "/file_to_write_readonly.txt".FsPath();
        using var file = await fs.CreateFile(filePath);
        file.Inner.Dispose();
        
        var readOnlyFs = new RealFileSystem(testRoot, true);
        await Assert.ThrowsAsync<FileSystemException.ReadOnly>(
            () => readOnlyFs.OpenFileWrite(filePath));
    }

    [Fact]
    public async Task OpenFileReadWrite_ExistingFile_ShouldReturnFileRW()
    {
        var filePath = "/file_to_readwrite.txt".FsPath();
        var initialContent = "Initial content";
        var initialBytes = Encoding.UTF8.GetBytes(initialContent);
        
        using var file = await fs.CreateFile(filePath);
        await file.Inner.WriteAsync(initialBytes);
        file.Inner.Dispose();
        
        var rwFile = await fs.OpenFileReadWrite(filePath);
        Assert.NotNull(rwFile);
        Assert.True(rwFile.Inner.IsReadable);
        Assert.True(rwFile.Inner.IsWritable);
        
        var buffer = new byte[rwFile.Inner.Length];
        await rwFile.Inner.ReadAsync(buffer);
        var readContent = Encoding.UTF8.GetString(buffer);
        Assert.Equal(initialContent, readContent);
        
        rwFile.Inner.Dispose();
    }

    [Fact]
    public async Task OpenFileReadWrite_NonExistingFile_ShouldCreateFile()
    {
        var filePath = "/new_readwrite_file.txt".FsPath();
        
        var rwFile = await fs.OpenFileReadWrite(filePath);
        Assert.NotNull(rwFile);
        Assert.True(rwFile.Inner.IsReadable);
        Assert.True(rwFile.Inner.IsWritable);
        
        var content = "New file content";
        var contentBytes = Encoding.UTF8.GetBytes(content);
        await rwFile.Inner.WriteAsync(contentBytes);
        rwFile.Inner.Dispose();
        
        Assert.True(await fs.FileExists(filePath));
    }

    [Fact]
    public async Task OpenFileReadWrite_ReadOnlyFileSystem_ShouldThrow()
    {
        var readOnlyFs = new RealFileSystem(testRoot, true);
        var filePath = "/readwrite_readonly.txt".FsPath();
        
        await Assert.ThrowsAsync<FileSystemException.ReadOnly>(
            () => readOnlyFs.OpenFileReadWrite(filePath));
    }

    #endregion

    #region File Content Tests

    [Fact]
    public async Task FileOperations_WriteAndReadBytes_ShouldWorkCorrectly()
    {
        var filePath = "/binary_operations.bin".FsPath();
        var data = new byte[] { 0x01, 0x02, 0x03, 0xFF, 0xFE, 0xFD };
        
        using var file = await fs.CreateFile(filePath);
        await file.Inner.WriteAsync(data);
        file.Inner.Dispose();
        
        using var readFile = await fs.OpenFileRead(filePath);
        Assert.NotNull(readFile);
        
        var buffer = new byte[readFile.Inner.Length];
        await readFile.Inner.ReadAsync(buffer);
        Assert.Equal(data, buffer);
        
        readFile.Inner.Dispose();
    }

    #endregion

    #region Directory Listing Tests

    [Fact]
    public async Task Entries_ShallowMode_ShouldReturnDirectChildren()
    {
        // Create test structure
        await fs.CreateDirectory("/test_entries".FsPath());
        await fs.CreateDirectory("/test_entries/subdir1".FsPath());
        await fs.CreateDirectory("/test_entries/subdir2".FsPath());
        
        var file1 = await fs.CreateFile("/test_entries/file1.txt".FsPath());
        file1.Inner.Dispose();
        var file2 = await fs.CreateFile("/test_entries/file2.txt".FsPath());
        file2.Inner.Dispose();
        
        // Create nested files that shouldn't be returned in shallow mode
        var nestedFile = await fs.CreateFile("/test_entries/subdir1/nested.txt".FsPath());
        nestedFile.Inner.Dispose();
        
        var entries = new List<FileEntry>();
        await foreach (var entry in fs.Entries("/test_entries".FsPath(), ListEntriesMode.ShallowAll))
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
        await fs.CreateDirectory("/test_recursive".FsPath());
        await fs.CreateDirectory("/test_recursive/subdir".FsPath());
        await fs.CreateDirectory("/test_recursive/subdir/deep".FsPath());
        
        var file1 = await fs.CreateFile("/test_recursive/root.txt".FsPath());
        file1.Inner.Dispose();
        var file2 = await fs.CreateFile("/test_recursive/subdir/nested.txt".FsPath());
        file2.Inner.Dispose();
        var file3 = await fs.CreateFile("/test_recursive/subdir/deep/deep.txt".FsPath());
        file3.Inner.Dispose();
        
        var entries = new List<FileEntry>();
        await foreach (var entry in fs.Entries("/test_recursive".FsPath(), ListEntriesMode.RecursiveAll))
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
        await fs.CreateDirectory("/test_filter".FsPath());
        
        var txtFile = await fs.CreateFile("/test_filter/document.txt".FsPath());
        txtFile.Inner.Dispose();
        var logFile = await fs.CreateFile("/test_filter/application.log".FsPath());
        logFile.Inner.Dispose();
        var binFile = await fs.CreateFile("/test_filter/program.exe".FsPath());
        binFile.Inner.Dispose();
        
        var entries = new List<FileEntry>();
        await foreach (var entry in fs.Entries("/test_filter".FsPath(), ListEntriesMode.Shallow("*.txt")))
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
        await fs.CreateDirectory("/subfs_test".FsPath());
        
        var subFs = fs.At("/subfs_test".FsPath());
        Assert.NotNull(subFs);
        Assert.Equal(fs.ReadOnly, subFs.ReadOnly);
        
        // Test that the sub-filesystem works
        using var file = await subFs.CreateFile("/file_in_sub.txt".FsPath());
        file.Inner.Dispose();
        
        // Verify the file exists in the original filesystem
        Assert.True(await fs.FileExists("/subfs_test/file_in_sub.txt".FsPath()));
    }

    [Fact]
    public async Task At_WithReadOnlyMode_ShouldCreateReadOnlyFileSystem()
    {
        var readOnlySubFs = fs.At("/readonly_subfs".FsPath(), FileSystemMode.ReadOnly);
        Assert.True(readOnlySubFs.ReadOnly);
        
        await Assert.ThrowsAsync<FileSystemException.ReadOnly>(
            () => readOnlySubFs.CreateFile("/file.txt".FsPath()));
    }

    [Fact]
    public async Task At_WithReadWriteMode_FromReadOnlyParent_ShouldThrow()
    {
        var readOnlyFs = new RealFileSystem(testRoot, true);
        await fs.CreateDirectory("/readwrite_attempt".FsPath());
        
        Assert.Throws<FileSystemException.ReadOnly>(
            () => readOnlyFs.At("/readwrite_attempt".FsPath(), FileSystemMode.ReadWrite));
    }

    [Fact]
    public async Task At_WithInheritMode_ShouldInheritParentMode()
    {
        var readOnlyFs = new RealFileSystem(testRoot, true);
        await fs.CreateDirectory("/inherit_test".FsPath());
        
        var subFs = readOnlyFs.At("/inherit_test".FsPath(), FileSystemMode.Inherit);
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
        await Assert.ThrowsAnyAsync<Exception>(
            () => fs.CreateFile("/././outside_root".FsPath()));
    }

    [Fact]
    public async Task AbsolutePath_OutsideRoot_ShouldThrowOrReturnNull()
    {
        try
        {
            var outsidePath = "/completely/different/path".FsPath();
            var result = await fs.FileExists(outsidePath);
            Assert.False(result);
        }
        catch (FileSystemException.NotFound)
        {
            // This is also acceptable behavior
        }
    }

    #endregion

    #region FileEntry Tests

    [Fact]
    public async Task FileRef_Operations_ShouldWorkCorrectly()
    {
        var filePath = "/fileref_test.txt".FsPath();
        var content = "FileRef test content";
        var contentBytes = Encoding.UTF8.GetBytes(content);
        
        // Create file and get as FileRef through Entries
        using var file = await fs.CreateFile(filePath);
        await file.Inner.WriteAsync(contentBytes);
        file.Inner.Dispose();
        
        var entries = new List<FileEntry>();
        await foreach (var entry in fs.Entries("/".FsPath(), ListEntriesMode.ShallowAll))
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
        readFile.Inner.Dispose();
        
        var writeFile = await fileRef.OpenWrite();
        Assert.NotNull(writeFile);
        // When opening for write, the file should be truncated/overwritten
        var newContent = "Updated content";
        var newContentBytes = Encoding.UTF8.GetBytes(newContent);
        // Ensure we start from the beginning
        writeFile.Inner.SetLength(0);
        await writeFile.Inner.WriteAsync(newContentBytes);
        writeFile.Inner.Dispose();
        
        var rwFile = await fileRef.OpenReadWrite();
        Assert.NotNull(rwFile);
        var updatedBuffer = new byte[rwFile.Inner.Length];
        await rwFile.Inner.ReadAsync(updatedBuffer);
        var updatedContent = Encoding.UTF8.GetString(updatedBuffer);
        Assert.Equal(newContent, updatedContent);
        rwFile.Inner.Dispose();
        
        Assert.True(await fileRef.Delete());
        Assert.False(await fileRef.Exists());
    }

    [Fact]
    public async Task Directory_At_ShouldCreateSubFileSystem()
    {
        var dirPath = "/directory_at_test".FsPath();
        var dir = await fs.CreateDirectory(dirPath);
        
        var subFs = dir.At();
        Assert.NotNull(subFs);
        
        var subFile = await subFs.CreateFile("/subfile.txt".FsPath());
        subFile.Inner.Dispose();
        
        // Verify file exists in parent filesystem
        Assert.True(await fs.FileExists("/directory_at_test/subfile.txt".FsPath()));
    }

    #endregion
}
