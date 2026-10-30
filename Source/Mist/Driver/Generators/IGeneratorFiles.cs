namespace Mist.Driver.Generators;

internal interface IGeneratorFiles
{
    bool Exists(string path);

    byte[] Read(string path);

    void Write(string path, byte[] content);

    IEnumerable<string> Files(string folder);
}