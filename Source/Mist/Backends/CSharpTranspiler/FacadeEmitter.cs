using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Mist.Backends.Contracts;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Written with System.Reflection.Metadata rather than compiled by Roslyn, because a facade is names and
/// nothing else: it can be built before the primary output exists, from the identity the primary
/// compilation already declares.
/// </summary>
internal static class FacadeEmitter
{
    private const TypeAttributes Forwarder = (TypeAttributes)0x00200000;

    public static ImmutableArray<byte> Emit(string name, string targetName, Version version, IEnumerable<ForwardedType> types)
    {
        var forwarded = types
            .Distinct()
            .OrderBy(type => type.NamespaceName, StringComparer.Ordinal)
            .ThenBy(type => type.TypeName, StringComparer.Ordinal)
            .ToList();

        var metadata = new MetadataBuilder();
        _ = metadata.AddModule(
            0,
            metadata.GetOrAddString($"{name}.dll"),
            metadata.GetOrAddGuid(Mvid(name, targetName, version, forwarded)),
            default,
            default);
        _ = metadata.AddAssembly(
            metadata.GetOrAddString(name), version, default, default, default, AssemblyHashAlgorithm.Sha1);
        var target = metadata.AddAssemblyReference(
            metadata.GetOrAddString(targetName), version, default, default, default, default);
        _ = metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var rows = new Dictionary<ForwardedType, ExportedTypeHandle>();
        foreach(var type in forwarded)
        {
            var separator = type.TypeName.LastIndexOf('/');
            if(separator < 0)
            {
                rows[type] = metadata.AddExportedType(
                    Forwarder,
                    metadata.GetOrAddString(type.NamespaceName),
                    metadata.GetOrAddString(type.TypeName),
                    target,
                    0);
            }
            else if(rows.TryGetValue(type with { TypeName = type.TypeName[..separator] }, out var encloser))
            {
                rows[type] = metadata.AddExportedType(
                    default, default, metadata.GetOrAddString(type.TypeName[(separator + 1)..]), encloser, 0);
            }
        }

        var image = new BlobBuilder();
        _ = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            strongNameSignatureSize: 0,
            deterministicIdProvider: ContentId).Serialize(image);
        return image.ToImmutableArray();
    }

    private static Guid Mvid(string name, string targetName, Version version, List<ForwardedType> forwarded)
    {
        var text = new StringBuilder()
            .Append(name).Append('\0')
            .Append(targetName).Append('\0')
            .Append(version).Append('\0');
        foreach(var type in forwarded)
        {
            _ = text.Append(type.NamespaceName).Append('\0').Append(type.TypeName).Append('\0');
        }

        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())).AsSpan(0, 16));
    }

    private static BlobContentId ContentId(IEnumerable<Blob> content)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(var blob in content)
        {
            hash.AppendData(blob.GetBytes());
        }

        return BlobContentId.FromHash(hash.GetHashAndReset().ToImmutableArray());
    }
}