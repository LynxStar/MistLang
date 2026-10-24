using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Mist.Backends.CSharpTranspiler;

internal static class AssemblyReferenceScan
{
    public static ImmutableArray<string> ReferencedNames(PEReader image)
    {
        if(!image.HasMetadata)
        {
            return [];
        }

        var reader = image.GetMetadataReader();
        return [.. reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name))];
    }

    public static Version? AssemblyVersion(PEReader image)
    {
        if(!image.HasMetadata)
        {
            return null;
        }

        var reader = image.GetMetadataReader();
        return reader.IsAssembly ? reader.GetAssemblyDefinition().Version : null;
    }
}