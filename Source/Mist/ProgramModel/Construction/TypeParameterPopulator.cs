using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Shared logic for creating <see cref="TypeParameter"/> instances from AST
/// type parameter list nodes. Used by both <see cref="TypeBuilder"/> (Phase 1,
/// type-level params) and <see cref="MethodBuilder"/> (Phase 2, method-level params).
/// </summary>
internal static class TypeParameterPopulator
{
    /// <summary>
    /// Creates <see cref="TypeParameter"/> instances from the AST type parameter list,
    /// adds them to the provided list, and registers them in the given scope.
    /// </summary>
    public static void Populate(
        List<TypeParameter> target, Scope scope, MistType? declaringType, TypeParameterListNode? typeParams)
    {
        if(typeParams is null)
        {
            return;
        }

        AddTypeParameter(target, scope, declaringType, typeParams.TypeParameter, ordinal: 0);

        for(var i = 0; i < typeParams.TypeParameters.Length; i++)
        {
            AddTypeParameter(target, scope, declaringType, typeParams.TypeParameters[i], ordinal: i + 1);
        }
    }

    private static void AddTypeParameter(
        List<TypeParameter> target, Scope scope, MistType? declaringType, TypeParameterNode node, int ordinal)
    {
        var name = node switch
        {
            RegularTypeParameterTypeParameterNode reg => reg.Name.Lexeme,
            DependentTypeParameterTypeParameterNode dep => dep.Name.Lexeme,
            NamedConstraintDependentTypeParameterTypeParameterNode named => named.Name.Lexeme,
            _ => throw new InvalidOperationException($"Unknown TypeParameterNode variant: {node.GetType().Name}")
        };

        var typeParam = new TypeParameter(name, ordinal, scope, declaringType);
        target.Add(typeParam);
        scope.AddLocalType(name, typeParam);
    }
}
