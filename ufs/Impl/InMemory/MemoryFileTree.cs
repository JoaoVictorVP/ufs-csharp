using System.Collections.Immutable;

namespace ufs.Impl.InMemory;

public abstract class MemoryFileTree
{
    public class File(string name, StreamWrapper stream, Directory parent) : MemoryFileTree
    {
        public string Name { get; } = name;
        public StreamWrapper Stream { get; private set; } = stream;
        public Directory Parent { get; } = parent;

        public FsPath Path()
        {
            var name = Name;
            if (Parent is not null)
                name = System.IO.Path.Combine(Parent.Path().Value, name);
            return new FsPath(name);
        }

        public void SwapStream(StreamWrapper newStream)
        {
            if (newStream is null)
                throw new ArgumentNullException(nameof(newStream));
            Stream.Dispose();
            Stream = newStream;
        }
    }
    public sealed class Root(bool readOnly = false) : Directory("/", readOnly)
    {
        public HashSet<FsPath> Tombstones { get; } = [];
    }
    public class Directory(string name, bool readOnly, Directory? parent = null) : MemoryFileTree
    {
        public string Name { get; } = name;
        public Directory? Parent { get; } = parent;
        public Dictionary<string, File> Files { get; } = [];
        public Dictionary<string, Directory> Directories { get; } = [];

        public Root GetRoot() => (this, Parent) switch
        {
            (Root root, _) => root,
            (_, Root root) => root,
            (_, Directory dir) => dir.GetRoot(),
            _ => throw new InvalidOperationException("Root directory not found.")
        };

        public bool ReadOnly { get; set; } = readOnly;

        public FsPath Path()
        {
            var name = Name;
            if (Parent is not null)
                name = System.IO.Path.Combine(Parent.Path().Value, name);
            return new FsPath(name);
        }

        public IEnumerable<(Directory? dir, ReadOnlyMemory<ReadOnlyMemory<char>>)> RecursiveWalk(FsPath path)
        {
            var segments = path.Segments(Path()).ToArray().AsMemory();
            yield return (this, segments);
            var cur = this;
            while (segments.IsEmpty is false)
            {
                var segment = segments.Span[0];
                if (cur.GetDirectory(segment.Span.ToString()) is not { } next)
                    yield break;
                cur = next;
                segments = segments[1..];
                yield return (cur, segments);
            }
        }
        public Directory? FindDirectory(FsPath path)
        {
            if(path.IsRoot)
                return this;
            var segments = path.Segments(Path()).GetEnumerator();
            var cur = this;
            while (segments.MoveNext())
            {
                if (cur.GetDirectory(segments.Current.Span.ToString()) is not { } next)
                    return null;
                cur = next;
            }
            return cur;
        }

        public IEnumerable<File> RecursiveFiles()
        {
            foreach (var file in Files.Values)
                yield return file;
            foreach (var dir in Directories.Values)
            {
                foreach (var file in dir.RecursiveFiles())
                    yield return file;
            }
        }

        public IEnumerable<MemoryFileTree> RecursiveEntries()
        {
            foreach (var file in Files.Values)
                yield return file;
            foreach (var dir in Directories.Values)
            {
                yield return dir;
                foreach (var entry in dir.RecursiveEntries())
                    yield return entry;
            }
        }

        public Directory? GetDirectory(string name)
        {
            if (Directories.TryGetValue(name, out var dir))
                return dir;
            return null;
        }
        public File? GetFile(string name)
        {
            if (Files.TryGetValue(name, out var file))
                return file;
            return null;
        }

        public Directory CreateDir(string name, bool readOnly = false)
        {
            if(name.Contains('/') || name.Contains('\\'))
                throw new ArgumentException("Directory name cannot contain slashes.", nameof(name));
            if (ReadOnly)
                throw new FileSystemException.ReadOnly(Name);
            if (readOnly is false && ReadOnly)
                throw new FileSystemException.ReadOnly(Name);
            var existing = GetDirectory(name);
            if (existing is not null)
                return existing;
            var dir = new Directory(name, readOnly, this);
            Directories[name] = dir;
            return dir;
        }
        public File CreateFile(string name, StreamWrapper stream)
        {
            if (ReadOnly)
                throw new FileSystemException.ReadOnly(Name);
            if (stream is null)
                throw new ArgumentNullException(nameof(stream));
            var existing = GetFile(name);
            if (existing is not null)
            {
                existing.SwapStream(stream);
                return existing;
            }
            var file = new File(name, stream, this);
            Files[name] = file;
            var tombstones = GetRoot().Tombstones;
            tombstones.Remove(file.Path());
            return file;
        }

        public void DeleteDir(Directory dir)
        {
            if (ReadOnly)
                throw new FileSystemException.ReadOnly(Name);
            if (dir is null)
                throw new ArgumentNullException(nameof(dir));
            if (Directories.Remove(dir.Name, out var removed) is false)
                return;
            var tombstones = GetRoot().Tombstones;
            foreach (var file in removed.RecursiveFiles())
            {
                Files.Remove(file.Name, out _);
                tombstones.Add(file.Path());
                file.Stream.Dispose();
            }
        }
        public void DeleteFile(File file)
        {
            if (ReadOnly)
                throw new FileSystemException.ReadOnly(Name);
            if (file is null)
                throw new ArgumentNullException(nameof(file));
            if (Files.Remove(file.Name, out var removed) is false)
                throw new FileSystemException.NotFound(file.Path().Value);
            var tombstones = GetRoot().Tombstones;
            tombstones.Add(removed.Path());
            removed.Stream.Dispose();
        }

        public void TombstoneFile(FsPath path)
        {
            var tombstones = GetRoot().Tombstones;
            tombstones.Add(path);
        }

        public bool IsTombstoned(FsPath path)
        {
            return GetRoot().Tombstones.Contains(path);
        }
    }
}
