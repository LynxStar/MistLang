using System.Collections.Immutable;

namespace Mist.Backends.CSharpTranspiler;

internal interface IAssemblyMetadataSource
{
    ImmutableArray<string> ReferencedNames(string path);

    Version? AssemblyVersion(string path);

    string? RuntimeConfig(string path);
}