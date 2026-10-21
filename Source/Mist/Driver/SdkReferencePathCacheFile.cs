using System.Diagnostics;

namespace Mist.Driver;

internal sealed class SdkReferencePathCacheFile(string path, TimeSpan lockTimeout)
{
    public SdkReferencePathCacheFile(string path)
        : this(path, TimeSpan.FromSeconds(2))
    {
    }

    public string? Read()
    {
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void Update(Func<string?, string> revise)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var transaction = Lock();
        Write(revise(Read()));
    }

    // The lock is a sibling that is never deleted: deleting it would let one process lock the removed
    // file while another creates and locks its replacement. The OS releases the lock with its handle, so
    // an owner that dies holds nothing, and a contender that times out skips one publication instead.
    private FileStream Lock()
    {
        var waited = Stopwatch.StartNew();
        while(true)
        {
            try
            {
                return new FileStream($"{path}.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch(IOException) when(waited.Elapsed < lockTimeout)
            {
                Thread.Sleep(10);
            }
        }
    }

    private void Write(string contents)
    {
        // Replacing the file whole means a concurrent reader sees the old entries or the new, never a
        // truncated path that could name an existing ancestor directory.
        var staged = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(staged, contents);
            File.Move(staged, path, overwrite: true);
        }
        finally
        {
            if(File.Exists(staged))
            {
                File.Delete(staged);
            }
        }
    }
}