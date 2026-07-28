using Mist.Generated;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Types;

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
        List<TypeParameter> target,
        ITypeNameScope scope,
        MistType? declaringType,
        string namespaceName,
        TypeParameterListNode? typeParams)
    {
        if(typeParams is null)
        {
            return;
        }

        AddTypeParameter(target, scope, declaringType, namespaceName, typeParams.TypeParameter, ordinal: 0);

        for(var i = 0; i < typeParams.TypeParameters.Length; i++)
        {
            AddTypeParameter(target, scope, declaringType, namespaceName, typeParams.TypeParameters[i], ordinal: i + 1);
        }
    }

    private static void AddTypeParameter(
        List<TypeParameter> target,
        ITypeNameScope scope,
        MistType? declaringType,
        string namespaceName,
        TypeParameterNode node,
        int ordinal)
    {
        var name = node.GetName().Lexeme;

        // Every ITypeNameScope is a Scope; the interface exists only to keep the DECLARING
        // capability off the kinds that cannot hold a type name.
        var typeParam = new TypeParameter(name, ordinal, namespaceName, (Scope)scope, declaringType);

        if(node is ParamsPackTypeParameterTypeParameterNode)
        {
            typeParam.HasParams = true;
        }

        target.Add(typeParam);
        scope.DeclareType(name, typeParam);
    }
}