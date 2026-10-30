namespace Mist.Driver.Generators;

internal sealed class GeneratorFiles : IGeneratorFiles
{
    public bool Exists(string path)
    {
        return File.Exists(path);
    }

    public byte[] Read(string path)
    {
        return File.ReadAllBytes(path);
    }

    public void Write(string path, byte[] content)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    public IEnumerable<string> Files(string folder)
    {
        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories);
    }
}