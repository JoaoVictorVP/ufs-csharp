
# Universal File System
A virtual file system that allows you to access files and directories across different storage backends seamlessly.
It supports local file systems, in memory ones, overlay file systems and even one backed by minio (ufs.minio package).
Everything is designed to be used extensively with async/await.

### How to use?
Just create an instance of your desired file system and use it normally, like so:
```cs
// Root is the actual physical place in your file system where you want this fs to be pointing at as his own root
// ReadOnly is, well, read-only, it's a safety mechanism to disallow changes to this fs (it's probably better to make a wrapping readonly file system later, but this should suffice for most use cases now)
var fs = new RealFileSystem(Root: "/home/user", ReadOnly: false);
// Relative paths are forbidden, but you actually don't quite need them, as this is already relative to the specified root
// So in this case, example.txt will be actually written to /home/user/example.txt, cool right?
await fs.WriteAllText("/example.txt");

// If you want to manually write things, you can as well like this:
using var file = fs.OpenReadWrite("/example.txt");
// Inner is the inner stream of the file
// It's not actually a real stream, necessarily, but it can be, it's a wrapper! And you can create your own wrappers if you desire a more tuned behavior, just make sure to implement things correctly
await file.Inner.WriteAsync(new byte[] { 1, 2, 3 });
// After writting to more slow storage mechanisms, like physical file streams or minio streams, it's important to flush your changes after, so the actual IO operations can be forwarded
await file.Inner.Flush();
```

You can also use in-memory file systems, and even an overlay file system where you put a lower one to be read-only and an upper one to be written to, merging the views.

And you can also use it with a minio backend in the `ufs.minio` package! It's very cool as it simplifies a bit how you would need to use minio in this case, it will behave like a "normal" filesystem. Well, kinda, directories are still not a thing in minio, so we need to "simulate" them in-memory while no files are actually written. Also, files that are not written and flushed don't persist either (for performance reasons), but we keep them in-memory to make sure everything works well enough and is responsive.

You can also use the special `MountFileSystem` for having a file system where you can stuff other file systems in it, and access like it was a normal one, like this:
```cs
// create your minio file system with the correct keys and etc
var minioFs = new MinioFileSystem("/", MinioClientWrapper, ReadOnly: false);

var tmpFs = new RealFileSystem("/tmp", ReadOnly: false);

var mfs = new MountFileSystem();
mfs.Mount("/", minioFs);
mfs.Mount("/tmp", mfs);

await mfs.WriteAllText("/tmp/file.txt", "This will be in your real file system!");
await mfs.WriteAllText("/otherdir/file.txt", "This will be written directly into minio!");
```
Cool, right?

### How to use? (ufs.web)
With `ufs.web 0.2.0` you can now also configure your ASP.NET Core webserver to provide your files natively to the users using an unified API like this ->
```
GET ufs/entries/{shallow|deep}/{filter} -> JSON[string]                 | 403FORBIDDEN
GET ufs/files/file.ext    -> stream (res)                | 404NOT_FOUND | 403FORBIDDEN
PUT ufs/files/file.ext    -> stream (req)                               | 403FORBIDDEN
DELETE ufs/files/file.ext -> ()                                         | 403FORBIDDEN
HEAD ufs/files/file.ext   -> ()           | 200OK        | 404NOT_FOUND | 403FORBIDDEN
```
To use it, simply inject a custom instance of yours of `IFileAuthorizer` for your DI, then call `webApp.MapUfs(configureEp)`, passing to configureEp a lambda (or function) that will configure the endpoints with your own security settings. For example:
```
webApp.MapUfs(ep => ep.RequireAuthorization());
```
This will be applied to each created endpoint, and if you omit it or pass an empty lambda, it will be devoid of any authorization.
Also inject your prefered IFileSystem in your DI, but in the keyed scope `"ufs.web"` to prevent clashes with other custom configs you may want to have in future.

You can create your IFileAuthorizer like this ->
```cs
public class ExampleFileAuthorizer(MyUserDatabase db) : IFileAuthorizer
{
    private readonly MyUserDatabase db = db;

    public async IAsyncEnumerable<FilePermissions> CheckPermissionsAsync(FsPath path, IFileSystem fs, HttpContext ctx, CancellationToken cancellationToken = default)
    {
        var perms = await db.GetPermissionsAsync(whatever);
        if (perms.CanRead)
            yield return FilePermissions.Read;
        if (perms.CanWrite)
            yield return FilePermissions.Write;
        if (perms.CanDelete)
            yield return FilePermissions.Delete;
        if (perms.MaxWrittableSize is long maxSize)
            yield return FilePermissions.MaxSize(maxSize);
        ... // Add other permissions as needed
    }
}
```
As you can see, you can inject whatever you want in your authorizer, and you can use async/await and yield permissions in your own pace if your want more heavy checks for things, etc. If you want something simple, you can as well, it's fine.
This will not be cached (for now), so you should optimize your things, but for file operations this should not be a big problem either way, you generally want them to be really safe.

If you want, you can also have access to utilities (currently 1). You can register them with `services.AddUfsUtilities()`.
Current utilities are ->
* IWebFsProvider: allow for you to generate a download link for a given path

### Release Notes
##### 0.3.3 (ufs)
Implements IParsable for FsPath.

##### 0.3.3 (ufs.web)
Fixes a silly mistake with the HttpContextAccessor injection, it should had injected the interface instead.

##### 0.1.0 (ufs/ufs.minio) / 0.3.0 (ufs.web)
Adds IWebFsProvider and `AddUfsUtilities` for configuring it.

##### 0.1.0 (ufs/ufs.minio) / 0.2.0 (ufs.web)
Releases `ufs.web`

##### 0.1.0
Added support for mount file system, allowing you to mount other file systems at specific paths.

This enables you to create complex file system structures by combining multiple file systems.

Also fixed some bugs in memory file system.

Also, adds some utilities for creating a real file system, like `RealFileSystem.AtTempDir()`, `RealFileSystem.AtApp()` or `RealFileSystem.AtWorkingDir()`.

##### 0.0.1
Initial release with basic functionality.
