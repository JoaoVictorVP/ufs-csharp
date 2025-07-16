using System;
using Minio;

namespace ufs.minio;

public static class MinioConfig
{
    public static IMinioClient CreateClient(string endpoint, string accessKey, string secretKey, bool ssl = true)
    {
        var minioClientFactory = new MinioClientFactory(config =>
        {
            config
                .WithEndpoint(endpoint)
                .WithCredentials(accessKey, secretKey)
                .WithSSL(ssl)
                .Build();
        });
        IMinioClient client = minioClientFactory.CreateClient();
        return client;
    }
}
