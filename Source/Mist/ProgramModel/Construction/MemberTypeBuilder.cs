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

    protected TypeReferenceResolver TypeResolver { get; }

    protected BaseTypeResolver BaseTypeResolver { get; }

    protected ConstraintResolver ConstraintResolver { get; }

    protected PropertyBuilder PropertyBuilder { get; }

    protected IndexerBuilder IndexerBuilder { get; }

    protected MethodBuilder MethodBuilder { get; }

    protected OperatorBuilder OperatorBuilder { get; }

    protected ParameterBuilder ParameterBuilder { get; }

    protected DelegateResolver DelegateResolver { get; }

    /// <summary>
    /// Builds spread-generated member families (Requirement 10): the aggregate
    /// <c>(TArgs...)</c> member plus its recorded projection family, stem-namespace
    /// reservation, and the <c>MIST_VAR_0007</c>/<c>MIST_VAR_0008</c> diagnostics. Shared
    /// with <see cref="PropertyBuilder"/> so field spreads (resolved by the concrete
    /// builder) and property spreads route through the same logic.
    /// </summary>
    protected SpreadMemberBuilder SpreadMemberBuilder { get; }

    protected MemberTypeBuilder(BuilderContext context, TypeReferenceResolver typeResolver)
    {
        Context = context;
        TypeResolver = typeResolver;
        BaseTypeResolver = new BaseTypeResolver(context, typeResolver);
        ConstraintResolver = new ConstraintResolver(context, typeResolver);
        var accessorBuilder = new AccessorBuilder(context);
        ParameterBuilder = new ParameterBuilder(typeResolver);
        DelegateResolver = new DelegateResolver(context, ParameterBuilder, typeResolver);
        SpreadMemberBuilder = new SpreadMemberBuilder(context, typeResolver);
        PropertyBuilder = new PropertyBuilder(context, typeResolver, accessorBuilder, SpreadMemberBuilder);
        IndexerBuilder = new IndexerBuilder(context, typeResolver, accessorBuilder, ParameterBuilder);
        MethodBuilder = new MethodBuilder(typeResolver, DelegateResolver, ConstraintResolver, context.Diagnostics);
        OperatorBuilder = new OperatorBuilder(context, typeResolver, DelegateResolver);
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