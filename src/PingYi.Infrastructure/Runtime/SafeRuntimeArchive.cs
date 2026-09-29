using System.Formats.Tar;
using System.IO.Compression;

namespace PingYi.Infrastructure;

/// <summary>Extract into a new staging directory. Links are materialized as checked in-tree copies, never followed while extracting.</summary>
public static class SafeRuntimeArchive
{
    internal static string ResolvePath(string root, string name)
    {
        name = name.Replace('\\', '/');
        var parts = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (name.StartsWith('/') || parts.Any(p => p != "." && (p is ".." || p.Contains(':') || p.Contains('\0') || p.EndsWith(' ') || p.EndsWith('.'))))
            throw new InvalidDataException("Unsafe runtime archive path.");
        var path = Path.GetFullPath(Path.Combine(root, name));
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Runtime archive path escaped staging.");
        return path;
    }

    public static async Task ExtractAsync(string archive, string name, string root, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        var used = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        long total = 0; var count = 0;
        async Task WriteAsync(string path, Stream input, long length)
        {
            if (++count > 20000 || length < 0 || length > 4L * 1024 * 1024 * 1024 ||
                (total += length) > 12L * 1024 * 1024 * 1024 || !used.Add(path))
                throw new InvalidDataException("Runtime archive exceeds limits or has duplicate entries.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
            var buffer = new byte[131072]; long written = 0;
            int bytes;
            while ((bytes = await input.ReadAsync(buffer, token)) != 0)
            {
                if ((written += bytes) > length) throw new InvalidDataException("Archive entry exceeded its declared size.");
                await output.WriteAsync(buffer.AsMemory(0, bytes), token);
            }
            if (written != length) throw new InvalidDataException("Truncated runtime archive entry.");
        }
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(archive);
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(ResolvePath(root, entry.FullName)); continue; }
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new InvalidDataException("ZIP symbolic links are not accepted.");
                await using var input = entry.Open();
                await WriteAsync(ResolvePath(root, entry.FullName), input, entry.Length);
            }
        }
        else if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            await using var file = File.OpenRead(archive);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            TarEntry? entry;
            while ((entry = await tar.GetNextEntryAsync(cancellationToken: token)) is not null)
            {
                if (entry.Name is "." or "./") continue;
                var path = ResolvePath(root, entry.Name);
                if (entry.EntryType == TarEntryType.Directory) { Directory.CreateDirectory(path); continue; }
                if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
                {
                    if (entry.LinkName.StartsWith('/') || entry.LinkName.StartsWith('\\')) throw new InvalidDataException("Absolute archive link.");
                    var target = entry.EntryType == TarEntryType.HardLink ? ResolvePath(root, entry.LinkName)
                        : ResolvePath(root, Path.GetRelativePath(root, Path.GetDirectoryName(path)!) + "/" + entry.LinkName);
                    if (!used.Add(path) || ++count > 20000) throw new InvalidDataException("Duplicate archive link.");
                    links.Add(path, target); continue;
                }
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                    throw new InvalidDataException("Unsupported runtime archive entry.");
                await WriteAsync(path, entry.DataStream ?? Stream.Null, entry.Length);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path,
                    entry.Mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                  UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute));
            }
        }
        else throw new InvalidDataException("Unsupported runtime archive type.");
        foreach (var (path, first) in links)
        {
            token.ThrowIfCancellationRequested();
            var target = first; var seen = new HashSet<string> { path };
            while (links.TryGetValue(target, out var next))
            {
                if (!seen.Add(target) || seen.Count > 32) throw new InvalidDataException("Cyclic archive link.");
                target = next;
            }
            if (!File.Exists(target)) throw new InvalidDataException("Archive link target is not a regular extracted file.");
            var size = new FileInfo(target).Length;
            if ((total += size) > 12L * 1024 * 1024 * 1024) throw new InvalidDataException("Archive links exceed extraction budget.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(target, path, false);
        }
    }
}
