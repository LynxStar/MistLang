using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Types;

/// <summary>
/// Phase 2 builder for type aliases. Resolves the aliased type reference
/// from the declaration's <c>RefinableTypeExpression</c>, wrapping in a
/// <see cref="RefinedType"/> when a refinement predicate is present.
/// </summary>
internal sealed class TypeAliasBuilder
{
    private readonly ScopeResolver scopeResolver;
    private readonly BuilderContext context;
    private readonly HashSet<TypeAlias> building = [];

    public TypeAliasBuilder(BuilderContext context, ScopeResolver scopeResolver)
    {
        this.context = context;
        this.scopeResolver = scopeResolver;
    }

    /// <summary>
    /// Resolves the type alias's modifiers and target type reference. Re-entrant: an alias
    /// whose target names a later-declared alias is completed on demand from
    /// <see cref="ScopeResolver.ExpandAlias(TypeAlias, IReadOnlyList{IMistType}?, Storm.Runtime.SyntaxNode?)"/>, so a
    /// second call for an already-built alias returns without work, and re-entering an alias
    /// still being built is the definition of a cycle -- reported at that alias's declaration,
    /// which resolves to an error type to stop the recursion.
    /// </summary>
    public void Build(TypeAlias typeDef)
    {
        if(typeDef.AliasedType is not null)
        {
            return;
        }

        if(!building.Add(typeDef))
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.CyclicTypeAlias, typeDef.DeclarationNode, null,
                MistType.StripArity(typeDef.Name));
            typeDef.AliasedType = ErrorType.Instance;
            typeDef.IsFailed = true;
            return;
        }

        try
        {
            BuildCore(typeDef);
        }
        finally
        {
            _ = building.Remove(typeDef);
        }
    }

    private void BuildCore(TypeAlias typeDef)
    {
        var decl = typeDef.DeclarationNode;
        var modifiers = ModifierBuilder.Build(decl, context.Diagnostics, typeDef.DeclaringType);

        typeDef.Modifiers = modifiers;

        var stampableClauses = decl.Constraints.IsDefaultOrEmpty ? [] : decl.Constraints.Where(clause => clause is GenericConstraintClauseWhereClauseNode generic
            && generic.Constraints.Constraints.Prepend(generic.Constraints.Constraint)
                .Any(constraint => constraint is StampableConstraintConstraintNode)).ToImmutableArray();
        new ConstraintResolver(context, scopeResolver).Resolve(stampableClauses, typeDef.TypeParametersList, typeDef.Scope);

        var refinableExpr = decl.Type;

        if(refinableExpr.RefinableSingleTypeExprs.Length > 0)
        {
            // Union type alias: resolve all constituents, each potentially refined.
            var constituents = new List<IMistType>(refinableExpr.RefinableSingleTypeExprs.Length + 1)
            {
                ResolvePossiblyRefined(refinableExpr.First, typeDef)
            };

            foreach(var constituent in refinableExpr.RefinableSingleTypeExprs)
            {
                constituents.Add(ResolvePossiblyRefined(constituent, typeDef));
            }

            context.ProgramModel.CompositeApplications.Record(CompositeKind.Union, constituents, refinableExpr, typeDef.Scope);
            SetAliasedType(typeDef, context.ProgramModel.TypeInterner.GetOrCreateUnionType(constituents));
            return;
        }

        SetAliasedType(typeDef, ResolvePossiblyRefined(refinableExpr.First, typeDef));
    }

    /// <summary>
    /// A cycle detected while resolving the target has already assigned <see cref="ErrorType"/>
    /// to THIS alias and reported it; overwriting that with the resolution result would let the
    /// cyclic alias masquerade as its (error-tainted) target.
    /// </summary>
    private static void SetAliasedType(TypeAlias typeDef, IMistType resolved)
    {
        typeDef.AliasedType ??= resolved;
    }

    /// <summary>
    /// Resolves a single refinable type expression. If a refinement predicate is
    /// present, wraps the resolved base type in a <see cref="RefinedType"/>.
    /// </summary>
    private IMistType ResolvePossiblyRefined(RefinableSingleTypeExprNode node, TypeAlias alias)
    {
        var resolution = scopeResolver.ResolveWithFailure(node.Type, alias.Scope);
        alias.IsFailed |= resolution.IsFailed;
        var baseType = resolution.Type;

        if(node.Refinement is not null)
        {
            return TypeInterner.CreateRefinedType(baseType, node.Refinement, alias.Name);
        }

        return baseType;
    }
}