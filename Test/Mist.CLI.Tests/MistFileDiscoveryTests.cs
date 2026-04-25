namespace Mist.CLI.Tests;

public class MistFileDiscoveryTests
{
    [Fact]
    public void DiscoverMistFiles_FindsFilesRecursively()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_test_{Guid.NewGuid():N}");
        try
        {
            _ = Directory.CreateDirectory(tempDir);
            _ = Directory.CreateDirectory(Path.Combine(tempDir, "sub"));

            File.WriteAllText(Path.Combine(tempDir, "main.mist"), "// main");
            File.WriteAllText(Path.Combine(tempDir, "sub", "lib.mist"), "// lib");
            File.WriteAllText(Path.Combine(tempDir, "readme.txt"), "not a mist file");

            var files = Directory.EnumerateFiles(tempDir, "*.mist", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(2, files.Count);
            Assert.All(files, f => Assert.EndsWith(".mist", f));
        }
        finally
        {
            if(Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void DiscoverMistFiles_EmptyDirectory_ReturnsEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_test_{Guid.NewGuid():N}");
        try
        {
            _ = Directory.CreateDirectory(tempDir);

            var files = Directory.EnumerateFiles(tempDir, "*.mist", SearchOption.AllDirectories).ToList();

            Assert.Empty(files);
        }
        finally
        {
            if(Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void DiscoverMistFiles_IgnoresNonMistFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_test_{Guid.NewGuid():N}");
        try
        {
            _ = Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "file.cs"), "// C#");
            File.WriteAllText(Path.Combine(tempDir, "file.txt"), "text");
            File.WriteAllText(Path.Combine(tempDir, "file.mist"), "// mist");

            var files = Directory.EnumerateFiles(tempDir, "*.mist", SearchOption.AllDirectories).ToList();

            _ = Assert.Single(files);
            Assert.EndsWith(".mist", files[0]);
        }
        finally
        {
            if(Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}