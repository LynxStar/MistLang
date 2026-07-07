using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Phase 2 builder for type aliases. Resolves the aliased type reference
/// from the declaration's <c>RefinableTypeExpression</c>.
/// Refinement predicates are ignored; the base type of each constituent is resolved.
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
            // Union type alias: resolve all constituents.
            var constituents = new List<IMistType>(refinableExpr.RefinableSingleTypeExprs.Length + 1)
            {
                typeResolver.Resolve(refinableExpr.First.Type, typeDef.Scope)
            };

            foreach(var constituent in refinableExpr.RefinableSingleTypeExprs)
            {
                constituents.Add(typeResolver.Resolve(constituent.Type, typeDef.Scope));
            }

            typeDef.AliasedType = context.ProgramModel.TypeInterner.GetOrCreateUnionType(constituents);
            return;
        }

        typeDef.AliasedType = typeResolver.Resolve(refinableExpr.First.Type, typeDef.Scope);
    }
}