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

    protected MemberTypeBuilder(BuilderContext context, TypeReferenceResolver typeResolver)
    {
        Context = context;
        TypeResolver = typeResolver;
        BaseTypeResolver = new BaseTypeResolver(context, typeResolver);
        ConstraintResolver = new ConstraintResolver(context, typeResolver);
        var accessorBuilder = new AccessorBuilder(context);
        ParameterBuilder = new ParameterBuilder(typeResolver);
        DelegateResolver = new DelegateResolver(context, ParameterBuilder);
        PropertyBuilder = new PropertyBuilder(context, typeResolver, accessorBuilder);
        IndexerBuilder = new IndexerBuilder(context, typeResolver, accessorBuilder, ParameterBuilder);
        MethodBuilder = new MethodBuilder(typeResolver, DelegateResolver, ConstraintResolver);
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