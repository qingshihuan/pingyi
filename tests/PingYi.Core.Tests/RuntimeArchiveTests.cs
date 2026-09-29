using System.Formats.Tar;
using System.IO.Compression;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class RuntimeArchiveTests
{
    [Theory]
    [InlineData("../outside")]
    [InlineData("/tmp/outside")]
    [InlineData("C:/outside")]
    [InlineData("folder/../../outside")]
    [InlineData("file:stream")]
    [InlineData("file. ")]
    public void Traversal_or_ambiguous_windows_paths_are_rejected(string name) =>
        Assert.Throws<InvalidDataException>(() => SafeRuntimeArchive.ResolvePath(Path.Combine(Path.GetTempPath(), "runtime-stage"), name));
    [Fact]
    public async Task Zip_writes_only_regular_files_and_rejects_traversal()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "input.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(zip.CreateEntry("../outside").Open()); writer.Write("invalid");
            }
            await Assert.ThrowsAsync<InvalidDataException>(() => SafeRuntimeArchive.ExtractAsync(archive, "input.zip", Path.Combine(root, "stage"), default));
            Assert.False(File.Exists(Path.Combine(root, "outside")));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Tar_safe_library_links_become_copies_and_zero_length_files_work()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "input.tar.gz");
            await using (var output = File.Create(archive))
            await using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
            using (var writer = new TarWriter(gzip))
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "lib/libtest.so.1") { DataStream = new MemoryStream("lib"u8.ToArray()) });
                writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "lib/libtest.so") { LinkName = "libtest.so.1" });
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "empty.txt") { DataStream = new MemoryStream() });
            }
            var stage = Path.Combine(root, "stage");
            await SafeRuntimeArchive.ExtractAsync(archive, "input.tar.gz", stage, default);
            Assert.Equal("lib", await File.ReadAllTextAsync(Path.Combine(stage, "lib/libtest.so")));
            Assert.Null(new FileInfo(Path.Combine(stage, "lib/libtest.so")).LinkTarget);
            Assert.Equal(0, new FileInfo(Path.Combine(stage, "empty.txt")).Length);
        }
        finally { Directory.Delete(root, true); }
    }
}
