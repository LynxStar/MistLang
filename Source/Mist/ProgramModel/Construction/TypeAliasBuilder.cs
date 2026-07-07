using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Phase 2 builder for type aliases. Resolves the aliased type reference
/// from the declaration's <c>RefinableTypeExpression</c>.
/// Refinement predicates and union variants are not yet supported.
/// </summary>
internal sealed class TypeAliasBuilder
{
    private readonly TypeReferenceResolver typeResolver;
    private readonly BuilderContext context;

    public TypeAliasBuilder(BuilderContext context, TypeReferenceResolver typeResolver)
    {
        this.context = context;
        this.typeResolver = typeResolver;
    }

    /// <summary>
    /// Resolves the type alias's modifiers and target type reference.
    /// </summary>
    public void Build(TypeAlias typeDef)
    {
        var decl = typeDef.DeclarationNode;
        typeDef.Modifiers = ModifierBuilder.Build(decl, context.Diagnostics);

        var refinableExpr = decl.Type;

        if(refinableExpr.RefinableSingleTypeExprs.Length > 0)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnknownTypeRefNode, refinableExpr, null, "UnionType");
            typeDef.AliasedType = ErrorType.Instance;
            return;
        }

        typeDef.AliasedType = typeResolver.Resolve(refinableExpr.First.Type, typeDef.Scope);
    }
}