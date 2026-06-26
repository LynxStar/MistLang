// Backward-compatible extension methods for TypeArgumentListNode.
// The grammar changed from:
//   TypeArgumentList = LESS_THAN TypeReference (COMMA TypeReference)* GREATER_THAN
// to:
//   TypeArgumentList = LESS_THAN TypeArgument (COMMA TypeArgument)* GREATER_THAN
//   TypeArgument = ... | type:TypeReference as TypeReferenceTypeArgument
//
// These extensions provide the old TypeReference/TypeReferences accessors so existing
// code can work with minimal changes until it is updated to handle dependent type arguments.

using System;
using System.Collections.Immutable;
using Storm.Runtime;

namespace Mist.Generated;

/// <summary>
/// Extension methods on <see cref="TypeArgumentListNode"/> for backward compatibility
/// with the old grammar structure where TypeArgumentList directly contained TypeReference nodes.
/// </summary>
public static class TypeArgumentListNodeExtensions
{
    /// <summary>
    /// Gets the first type argument as a <see cref="TypeReferenceNode"/>.
    /// For <see cref="TypeReferenceTypeArgumentTypeArgumentNode"/> variants, extracts the inner type reference.
    /// For other variants (literal type arguments), throws because they cannot be treated as type references.
    /// </summary>
    public static TypeReferenceNode GetTypeReference(this TypeArgumentListNode node)
    {
        return ExtractTypeReference(node.TypeArgument);
    }

    /// <summary>
    /// Gets the remaining type arguments as <see cref="TypeReferenceNode"/> instances.
    /// For <see cref="TypeReferenceTypeArgumentTypeArgumentNode"/> variants, extracts the inner type references.
    /// For other variants (literal type arguments), throws because they cannot be treated as type references.
    /// </summary>
    public static ImmutableArray<TypeReferenceNode> GetTypeReferences(this TypeArgumentListNode node)
    {
        var builder = ImmutableArray.CreateBuilder<TypeReferenceNode>(node.TypeArguments.Length);
        foreach(var arg in node.TypeArguments)
        {
            builder.Add(ExtractTypeReference(arg));
        }
        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Extracts a <see cref="TypeReferenceNode"/> from a <see cref="TypeArgumentNode"/>.
    /// Only <see cref="TypeReferenceTypeArgumentTypeArgumentNode"/> is supported for
    /// backward-compatible type reference extraction. Other variants (literal type arguments)
    /// cannot be treated as type references and will throw.
    /// </summary>
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