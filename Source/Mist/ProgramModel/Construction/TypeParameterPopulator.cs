using Mist.Common.Diagnostics;
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
    ///
    /// Type-parameter packs (<c>params TArgs</c> / <c>Type[]:TArgs</c> /
    /// <c>Type[N]:TArgs</c>) are classified here: a <c>params</c> pack is always the
    /// list's single bracket-free <see cref="TypeParameterPackKind.Unbracketed"/> pack;
    /// a lone explicit <c>Type[]:</c> pack (the only pack in the list) is also
    /// <see cref="TypeParameterPackKind.Unbracketed"/> by inverse sugar; any other
    /// explicit <c>Type[]:</c> pack is <see cref="TypeParameterPackKind.Bracketed"/>.
    ///
    /// Arity is the number of declared slots, which is exactly the number of
    /// <see cref="TypeParameter"/> instances added here: each pack occupies one slot
    /// regardless of how many types it later absorbs (Requirement 3.6). So
    /// <c>Foo&lt;TVal, Type[]:TArgs, params CtorArgs, Type[]:Args3, TRes&gt;</c> adds five
    /// parameters and therefore has arity 5.
    ///
    /// When a <paramref name="diagnostics"/> bag is supplied, this method enforces the
    /// at-most-one-Unbracketed_Pack rule (Requirements 3.4, 3.5), reporting
    /// <see cref="VariadicsDiagnostics.MultipleParamsPacks"/> when a type-parameter list
    /// declares a second unbracketed pack. Callers whose list kind is validated by a
    /// later task (for example method type-parameter lists) may omit the bag.
    /// </summary>
    public static void Populate(
        List<TypeParameter> target,
        Scope scope,
        MistType? declaringType,
        TypeParameterListNode? typeParams,
        DiagnosticBag? diagnostics = null)
    {
        if(typeParams is null)
        {
            return;
        }

        // Gather the declared nodes in declaration order.
        var nodes = new List<TypeParameterNode> { typeParams.TypeParameter };
        nodes.AddRange(typeParams.TypeParameters);

        // A lone explicit `Type[]:` pack becomes the unbracketed pack by inverse
        // sugar, so classification needs the total pack count across the whole list.
        var packCount = nodes.Count(IsPackNode);

        // Track the nodes classified as the list's unbracketed (`params`) pack so a
        // surplus one can be reported precisely at its own declaration site.
        var unbracketedPackNodes = new List<TypeParameterNode>();

        for(var ordinal = 0; ordinal < nodes.Count; ordinal++)
        {
            var typeParam = AddTypeParameter(target, scope, declaringType, nodes[ordinal], ordinal, packCount);
            if(typeParam.IsUnbracketedPack)
            {
                unbracketedPackNodes.Add(nodes[ordinal]);
            }
        }

        // Requirement 3.4 / 3.5: at most one Unbracketed_Pack per type-parameter list.
        // The first unbracketed pack is the permitted bracket-free slot; a second one is
        // the violation, so the diagnostic points at that second declaration.
        if(diagnostics is not null && unbracketedPackNodes.Count > 1)
        {
            diagnostics.ReportAtNode(
                VariadicsDiagnostics.MultipleParamsPacks, unbracketedPackNodes[1], null, "type-parameter");
        }
    }

    private static TypeParameter AddTypeParameter(
        List<TypeParameter> target,
        Scope scope,
        MistType? declaringType,
        TypeParameterNode node,
        int ordinal,
        int packCount)
    {
        var name = node.GetName().Lexeme;
        var typeParam = new TypeParameter(name, ordinal, scope, declaringType);

        switch(node)
        {
            // `params TArgs` is sugar for `Type[]:TArgs` (unknown length) and is
            // always the list's single bracket-free Unbracketed_Pack.
            case ParamsPackTypeParameterTypeParameterNode:
                typeParam.PackKind = TypeParameterPackKind.Unbracketed;
                break;

            // `Type[]:TArgs` / `Type[N]:TArgs`. A lone pack (the only pack in the
            // list) is the Unbracketed_Pack by inverse sugar; otherwise it is a
            // Bracketed_Pack supplied in brackets at the use site.
            case ExplicitPackTypeParameterTypeParameterNode explicitPack:
                typeParam.PackKind = packCount == 1
                    ? TypeParameterPackKind.Unbracketed
                    : TypeParameterPackKind.Bracketed;
                typeParam.FixedPackLength = GetFixedPackLength(explicitPack.ArrayRank);
                break;

            // Regular (`T`) and dependent (`N:int`, `N:Constraint`) parameters are
            // not packs and keep the default TypeParameterPackKind.None.
            default:
                break;
        }

        target.Add(typeParam);
        scope.AddLocalType(name, typeParam);
        return typeParam;
    }

    /// <summary>
    /// True when the node declares a type-parameter pack (either the <c>params</c>
    /// sugar or the explicit <c>Type[]:</c> / <c>Type[N]:</c> form).
    /// </summary>
    private static bool IsPackNode(TypeParameterNode node)
    {
        return node is ParamsPackTypeParameterTypeParameterNode or ExplicitPackTypeParameterTypeParameterNode;
    }

    /// <summary>
    /// Extracts the fixed pack length <c>N</c> from an explicit pack's array-rank
    /// suffix: a sized rank (<c>Type[N]</c>) yields <c>N</c>; an unsized rank
    /// (<c>Type[]</c>, unknown length) yields <c>null</c>.
    /// </summary>
    private static long? GetFixedPackLength(ArrayRankSuffixNode rank)
    {
        return rank switch
        {
            SizedArrayRankArrayRankSuffixNode sized when long.TryParse(sized.Size.Lexeme, out var length) => length,
            _ => null,
        };
    }
}