using System.Collections.Immutable;

namespace Mist.Generated;

/// <summary>
/// Reads type arguments as <see cref="TypeReferenceNode"/>s, the first through <see cref="GetTypeReference"/>
/// and the rest through <see cref="GetTypeReferences"/>, for call sites that take no literal (dependent) type
/// argument; a literal one throws.
/// </summary>
public static class TypeArgumentListNodeExtensions
{
    public static TypeReferenceNode GetTypeReference(this TypeArgumentListNode node)
    {
        return ExtractTypeReference(node.TypeArgument);
    }

    public static ImmutableArray<TypeReferenceNode> GetTypeReferences(this TypeArgumentListNode node)
    {
        var builder = ImmutableArray.CreateBuilder<TypeReferenceNode>(node.TypeArguments.Length);
        foreach(var arg in node.TypeArguments)
        {
            builder.Add(ExtractTypeReference(arg));
        }

        return builder.MoveToImmutable();
    }

    private static TypeReferenceNode ExtractTypeReference(TypeArgumentNode arg)
    {
        return arg switch
        {
            TypeReferenceTypeArgumentTypeArgumentNode typeRefArg => typeRefArg.Type,
            _ => throw new InvalidOperationException(
                $"Cannot extract TypeReferenceNode from TypeArgumentNode variant: {arg.GetType().Name}. " +
                "This type argument is a literal value, not a type reference."),
        };
    }
}