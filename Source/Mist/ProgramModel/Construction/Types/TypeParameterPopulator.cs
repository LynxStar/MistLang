using Mist.Generated;
using Mist.ProgramModel.Model;
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
    /// Creates <see cref="TypeParameter"/> instances from the AST type parameter list and adds them
    /// to <paramref name="declaration"/>, which is where lookup reads them from.
    /// </summary>
    /// <param name="declaringScope">
    /// Not taken from the declaration: a method's scope exists before the final
    /// <see cref="Model.Members.Method"/> it will belong to, so there is a caller for whom the
    /// declaration cannot yet name it.
    /// </param>
    /// <param name="declaringType">
    /// Null when the declaration is not a type, as an extension block is not.
    /// </param>
    public static void Populate(
        IHasTypeParameters declaration,
        Scope declaringScope,
        MistType? declaringType,
        TypeParameterListNode? typeParams)
    {
        if(typeParams is null)
        {
            return;
        }

        AddTypeParameter(declaration, declaringScope, declaringType, typeParams.TypeParameter, ordinal: 0);

        for(var i = 0; i < typeParams.TypeParameters.Length; i++)
        {
            AddTypeParameter(declaration, declaringScope, declaringType, typeParams.TypeParameters[i], ordinal: i + 1);
        }
    }

    private static void AddTypeParameter(
        IHasTypeParameters declaration,
        Scope declaringScope,
        MistType? declaringType,
        TypeParameterNode node,
        int ordinal)
    {
        var name = node.GetName().Lexeme;

        var typeParam = new TypeParameter(name, ordinal, declaringScope, declaringType);

        if(node is ParamsPackTypeParameterTypeParameterNode)
        {
            typeParam.HasParams = true;
        }

        if(node is DependentTypeParameterTypeParameterNode
            or NamedConstraintDependentTypeParameterTypeParameterNode)
        {
            typeParam.IsDependent = true;
        }

        declaration.TypeParametersList.Add(typeParam);

        // Only scopes that keep their own table need telling. The ones backed by the declaration
        // these were just added to will read the list back out, and telling them as well would be
        // the duplicate store this exists to avoid.
        if(declaringScope is ITypeNameScope declaring)
        {
            declaring.DeclareType(name, typeParam);
        }
    }
}