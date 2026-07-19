using Mist.Generated;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Base class for Phase 2 builders that resolve member-bearing types (classes,
/// structs, abstracts). Provides shared infrastructure for base type resolution,
/// property building, and common modifier helpers.
/// </summary>
internal abstract class MemberTypeBuilder
{
    protected BuilderContext Context { get; }

    protected ScopeResolver ScopeResolver { get; }

    protected BaseTypeResolver BaseTypeResolver { get; }

    protected ConstraintResolver ConstraintResolver { get; }

    protected PropertyBuilder PropertyBuilder { get; }

    protected IndexerBuilder IndexerBuilder { get; }

    protected MethodBuilder MethodBuilder { get; }

    protected OperatorBuilder OperatorBuilder { get; }

    protected ParameterBuilder ParameterBuilder { get; }

    protected DelegateResolver DelegateResolver { get; }

    protected MemberTypeBuilder(BuilderContext context, ScopeResolver scopeResolver)
    {
        Context = context;
        ScopeResolver = scopeResolver;
        BaseTypeResolver = new BaseTypeResolver(context, scopeResolver);
        ConstraintResolver = new ConstraintResolver(context, scopeResolver);
        var accessorBuilder = new AccessorBuilder(context);
        ParameterBuilder = new ParameterBuilder(scopeResolver);
        DelegateResolver = new DelegateResolver(context, ParameterBuilder, scopeResolver);
        PropertyBuilder = new PropertyBuilder(context, scopeResolver, accessorBuilder);
        IndexerBuilder = new IndexerBuilder(context, scopeResolver, accessorBuilder, ParameterBuilder);
        MethodBuilder = new MethodBuilder(scopeResolver, DelegateResolver, ConstraintResolver);
        OperatorBuilder = new OperatorBuilder(context, scopeResolver, DelegateResolver);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Maps an optional <see cref="OwnershipSpecifierNode"/> to an <see cref="OwnershipKind"/>.
    /// </summary>
    internal static OwnershipKind ResolveOwnership(OwnershipSpecifierNode? specifier)
    {
        return specifier switch
        {
            OwnedSpecifierOwnershipSpecifierNode => OwnershipKind.Owned,
            BorrowedSpecifierOwnershipSpecifierNode => OwnershipKind.Borrowed,
            _ => OwnershipKind.Unrestricted
        };
    }
}