using System;
using ufs;

namespace ufs_tests;

public class FsPathTests
{
    [Fact]
    public void TryEncodingDecodingPath()
    {
        var path = "/path/to/file with spaces and special chars !@#$%^&*()".FsPath();
        var encoded = path.UriEncoded();
        var decoded = encoded.UriDecodedFsPath();
        Assert.Equal(path.Value, decoded.Value);
    }
}
