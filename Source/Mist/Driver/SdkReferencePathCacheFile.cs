namespace Mist.Driver;

internal sealed class SdkReferencePathCacheFile(string path)
{
    public string? Read()
    {
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void Write(string contents)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

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