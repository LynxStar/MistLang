// Backward-compatible extension methods for TypeParameterNode.
// The grammar changed from:
//   TypeParameter = name:IDENTIFIER
// to:
//   TypeParameter
//       = name:IDENTIFIER COLON valueType:PrimitiveType           as DependentTypeParameter
//       | name:IDENTIFIER COLON constraintName:IDENTIFIER         as NamedConstraintDependentTypeParameter
//       | name:IDENTIFIER                                          as RegularTypeParameter
//
// These extensions provide the old Name accessor so existing code can work
// with minimal changes until it is updated to handle dependent type parameters.

using Storm.Runtime;

namespace Mist.Generated;

/// <summary>
/// Extension methods on <see cref="TypeParameterNode"/> for backward compatibility
/// with the old grammar structure where TypeParameter had a direct Name property.
/// </summary>
public static class TypeParameterNodeExtensions
{
    /// <summary>
    /// Gets the Name token from a TypeParameterNode regardless of which alternative it is.
    /// </summary>
    public static Token GetName(this TypeParameterNode node)
    {
        return node switch
        {
            DependentTypeParameterTypeParameterNode dep => dep.Name,
            NamedConstraintDependentTypeParameterTypeParameterNode named => named.Name,
            RegularTypeParameterTypeParameterNode reg => reg.Name,
            _ => throw new System.InvalidOperationException(
                $"Unknown TypeParameterNode variant: {node.GetType().Name}"),
        };
    }
}