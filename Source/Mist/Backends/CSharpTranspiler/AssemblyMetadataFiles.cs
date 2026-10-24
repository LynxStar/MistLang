using System.Collections.Immutable;
using System.Reflection.PortableExecutable;

namespace Mist.Backends.CSharpTranspiler;

internal sealed class AssemblyMetadataFiles : IAssemblyMetadataSource
{
    public static AssemblyMetadataFiles Instance { get; } = new();

    public ImmutableArray<string> ReferencedNames(string path)
    {
        return Read(path, AssemblyReferenceScan.ReferencedNames, []);
    }

    public Version? AssemblyVersion(string path)
    {
        return Read(path, AssemblyReferenceScan.AssemblyVersion, null);
    }

    public string? RuntimeConfig(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch(Exception exception) when(exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static T Read<T>(string path, Func<PEReader, T> read, T unreadable)
    {
        if(!File.Exists(path))
        {
            return unreadable;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var image = new PEReader(stream);
            return read(image);
        }
        catch(Exception exception) when(exception is BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            return unreadable;
        }
    }
}