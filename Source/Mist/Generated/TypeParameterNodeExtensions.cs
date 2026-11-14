using Storm.Runtime;

namespace Mist.Generated;

public static class TypeParameterNodeExtensions
{
    /// <summary>
    /// The name token, whichever alternative this is.
    /// </summary>
    public static Token GetName(this TypeParameterNode node)
    {
        return node switch
        {
            DependentTypeParameterTypeParameterNode dep => dep.Name,
            NamedConstraintDependentTypeParameterTypeParameterNode named => named.Name,
            RegularTypeParameterTypeParameterNode reg => reg.Name,
            ParamsPackTypeParameterTypeParameterNode pack => pack.Name,
            ExplicitPackTypeParameterTypeParameterNode explicitPack => explicitPack.Name,
            _ => throw new InvalidOperationException(
                $"Unknown TypeParameterNode variant: {node.GetType().Name}"),
        };
    }
}