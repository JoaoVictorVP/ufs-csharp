using System.Runtime.CompilerServices;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace ufs.minio;

public record MinioClientWrapper(IMinioClient Client, string BucketName)
{
    static readonly HttpClient http = new();

    async Task EnsureBucket()
    {
        var args = new BucketExistsArgs().WithBucket(BucketName);
        var exists = await Client.BucketExistsAsync(args);
        if (exists is false)
            await Client.MakeBucketAsync(new MakeBucketArgs()
                .WithBucket(BucketName));
        else
            return;
        exists = await Client.BucketExistsAsync(args);
        if (exists is false)
            throw new Exception("Error creating bucket: " + BucketName);
    }

    public async Task<bool> Upload(string objectName, Stream stream, string contentType = "application/octet-stream")
    {
        await EnsureBucket();
        var args = new PutObjectArgs()
            .WithBucket(BucketName)
            .WithObject(objectName)
            .WithStreamData(stream)
            .WithObjectSize(stream.Length)
            .WithContentType(contentType);
        try
        {
            await Client.PutObjectAsync(args).ConfigureAwait(false);
            return true;
        }
        catch (MinioException)
        {
            return false;
        }
    }

    public async Task<(Stream stream, long length)> Get(string objectName)
    {
        var get = new PresignedGetObjectArgs()
            .WithBucket(BucketName)
            .WithObject(objectName)
            .WithExpiry(60 * 60);
        try
        {
            var url = await Client.PresignedGetObjectAsync(get).ConfigureAwait(false);
            var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            long length = response.Content.Headers.ContentLength ?? 0;
            return (await response.Content.ReadAsStreamAsync().ConfigureAwait(false), length);
        }
        catch (Exception)
        {
            return (Stream.Null, 0);
        }
    }

    public async Task<bool> Delete(string objectName)
    {
        var args = new RemoveObjectArgs()
            .WithBucket(BucketName)
            .WithObject(objectName);
        try
        {
            await Client.RemoveObjectAsync(args).ConfigureAwait(false);
            return true;
        }
        catch (MinioException)
        {
            return false;
        }
    }

    public async Task<bool> Exists(string objectName)
    {
        var args = new StatObjectArgs()
            .WithBucket(BucketName)
            .WithObject(objectName);
        try
        {
            await Client.StatObjectAsync(args).ConfigureAwait(false);
            return true;
        }
        catch (MinioException)
        {
            return false;
        }
    }

    public async Task<FileStatus> Stat(string objectName)
    {
        var args = new StatObjectArgs()
            .WithBucket(BucketName)
            .WithObject(objectName);
        try
        {
            var stat = await Client.StatObjectAsync(args).ConfigureAwait(false);
            if (stat.DeleteMarker)
                return FileStatus.Deleted;
            return FileStatus.Exists;
        }
        catch (MinioException)
        {
            return FileStatus.NotFound;
        }
    }
    
    public async IAsyncEnumerable<(string objectName, bool isDir)> ListObjects(string prefix = "", bool recursive = false, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var args = new ListObjectsArgs()
            .WithBucket(BucketName)
            .WithPrefix(prefix)
            .WithRecursive(recursive);
        await foreach (var item in
            Client.ListObjectsEnumAsync(args, cancellationToken).ConfigureAwait(false))
        {
                yield return (item.Key, item.IsDir);
        }
    }
}
