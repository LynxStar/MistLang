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

    protected PropertyBuilder PropertyBuilder { get; }

    protected MemberTypeBuilder(BuilderContext context, TypeReferenceResolver typeResolver)
    {
        Context = context;
        TypeResolver = typeResolver;
        BaseTypeResolver = new BaseTypeResolver(context, typeResolver);
        PropertyBuilder = new PropertyBuilder(context, typeResolver);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Maps an optional <see cref="OwnershipSpecifierNode"/> to an <see cref="OwnershipKind"/>.
    /// </summary>
    protected static OwnershipKind ResolveOwnership(OwnershipSpecifierNode? specifier)
    {
        return specifier switch
        {
            OwnedSpecifierOwnershipSpecifierNode => OwnershipKind.Owned,
            BorrowedSpecifierOwnershipSpecifierNode => OwnershipKind.Borrowed,
            _ => OwnershipKind.Unrestricted
        };
    }
}