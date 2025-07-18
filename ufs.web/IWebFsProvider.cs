using System;

namespace ufs.web;

public interface IWebFsProvider
{
    ValueTask<string> GetDownloadUrl(FsPath path);
}
