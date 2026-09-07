using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
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
        TypeParameterListNode? typeParams,
        DiagnosticBag diagnostics)
    {
        if(typeParams is null)
        {
            return;
        }

        var nodes = new List<TypeParameterNode>(1 + typeParams.TypeParameters.Length) { typeParams.TypeParameter };
        nodes.AddRange(typeParams.TypeParameters);

        // A lone explicit pack is bracket-free as well, so whether THIS parameter needs brackets is
        // a fact about the whole list. Nothing here can be decided one parameter at a time.
        var lonePack = nodes.Count(IsPack) == 1;

        TypeParameter? unbracketed = null;

        for(var ordinal = 0; ordinal < nodes.Count; ordinal++)
        {
            var typeParam = AddTypeParameter(
                declaration, declaringScope, declaringType, nodes[ordinal], ordinal, lonePack);

            if(typeParam.PackKind != TypePackKind.Unbracketed)
            {
                continue;
            }

            if(unbracketed is not null)
            {
                diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.MultipleUnbracketedPacks, nodes[ordinal], null,
                    typeParam.Name, unbracketed.Name);

                // Demoted so the rest of the pipeline still reads an unambiguous list. The
                // diagnostic is the verdict; leaving two absorbing slots behind would make every
                // later question about this declaration unanswerable as well.
                typeParam.PackKind = TypePackKind.Bracketed;
                continue;
            }

            unbracketed = typeParam;
        }
    }

    private static bool IsPack(TypeParameterNode node)
    {
        return node is ParamsPackTypeParameterTypeParameterNode or ExplicitPackTypeParameterTypeParameterNode;
    }

    private static TypeParameter AddTypeParameter(
        IHasTypeParameters declaration,
        Scope declaringScope,
        MistType? declaringType,
        TypeParameterNode node,
        int ordinal,
        bool lonePack)
    {
        var name = node.GetName().Lexeme;

        var typeParam = new TypeParameter(name, ordinal, declaringScope, declaringType);

        switch(node)
        {
            // A pack is dependent for the same reason `Size:int` is, and this is the whole of the
            // design rather than a convenience: its domain is a comptime `Type[]`, so what fills
            // the slot is a value and the construction's identity is that value. `params TArgs`
            // is sugar for `Type[]:TArgs`.
            case ParamsPackTypeParameterTypeParameterNode:
                typeParam.PackKind = TypePackKind.Unbracketed;
                typeParam.IsDependent = true;
                break;

            case ExplicitPackTypeParameterTypeParameterNode explicitPack:
                typeParam.PackKind = lonePack ? TypePackKind.Unbracketed : TypePackKind.Bracketed;
                typeParam.PackLength = FixedLength(explicitPack.ArrayRank);
                typeParam.IsDependent = true;
                break;

            case DependentTypeParameterTypeParameterNode:
            case NamedConstraintDependentTypeParameterTypeParameterNode:
                typeParam.IsDependent = true;
                break;
        }

        declaration.TypeParametersList.Add(typeParam);

        // Only scopes that keep their own table need telling. The ones backed by the declaration
        // these were just added to will read the list back out, and telling them as well would be
        // the duplicate store this exists to avoid.
        if(declaringScope is ITypeNameScope declaring)
        {
            declaring.DeclareType(name, typeParam);
        }

        return typeParam;
    }

    private static int? FixedLength(ArrayRankSuffixNode arrayRank)
    {
        return arrayRank is SizedArrayRankArrayRankSuffixNode sized
            && int.TryParse(sized.Size.Lexeme, out var length)
                ? length
                : null;
    }
}