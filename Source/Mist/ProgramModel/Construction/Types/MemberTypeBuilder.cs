using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Types;

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
        ParameterBuilder = new ParameterBuilder(scopeResolver, context.Attributes, context.Diagnostics);
        DelegateResolver = new DelegateResolver(context, ParameterBuilder, scopeResolver);
        PropertyBuilder = new PropertyBuilder(context, scopeResolver, accessorBuilder);
        IndexerBuilder = new IndexerBuilder(context, scopeResolver, accessorBuilder, ParameterBuilder, ConstraintResolver);
        MethodBuilder = new MethodBuilder(context, scopeResolver, DelegateResolver, ConstraintResolver);
        OperatorBuilder = new OperatorBuilder(context, scopeResolver, DelegateResolver);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Entry points
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Records the method just resolved on its assembly if it has the shape of an entry point.
    /// <para>
    /// Called after each method is resolved and judging nothing: two of them, or one beside a file
    /// of top-level statements, is settled by the project's manifest, which the binder does not
    /// read.
    /// </para>
    /// </summary>
    protected static void RecordEntryPointIfPresent(MemberType typeDef)
    {
        var method = typeDef.MethodsList.LastOrDefault();

        if(method is null || !IsEntryPoint(method) || typeDef.Assembly.EntryPoints.Contains(method))
        {
            return;
        }

        typeDef.Assembly.EntryPoints.Add(method);
    }

    /// <summary>
    /// Whether a method has the shape of an entry point: <c>static</c>, named <c>Main</c>,
    /// at most one parameter, returning void or int. Accessibility does not participate --
    /// a non-public <c>Main</c> is a valid entry point.
    /// </summary>
    private static bool IsEntryPoint(Method method)
    {
        if(!method.Modifiers.IsStatic || method.Name != "Main")
        {
            return false;
        }

        if(method.ParametersList.Count is not (0 or 1))
        {
            return false;
        }

        return method.ReturnType.FullyQualifiedName
            is "Standard.Void"
            or "Standard.Int32";
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
            FrozenSpecifierOwnershipSpecifierNode => OwnershipKind.Frozen,
            _ => OwnershipKind.Unrestricted
        };
    }

    /// <summary>
    /// Whether a specifier asks for pass-by-reference. Not folded into
    /// <see cref="ResolveOwnership"/> because <c>ref</c> is orthogonal to the ownership tier rather
    /// than another value of it, which is why <c>readonly ref</c> composes as two flags on
    /// <see cref="ParameterModifier"/> instead of naming a fifth tier.
    /// <para>
    /// ONLY A LOCAL DECLARATION REACHES HERE WITH A REF. Every other rule carrying an ownership
    /// specifier -- a method's return, a field -- puts <c>Modifier*</c> ahead of it, and
    /// <c>Modifier</c> admits <c>ref</c> and matches greedily, so the token is taken as a modifier
    /// and surfaces on <see cref="MemberModifiers.IsRef"/> instead. Both ref spellings
    /// answer true regardless, since a local's greedy <c>readonly</c> token splits
    /// <c>readonly ref</c> into a modifier plus a bare specifier rather than the combined node.
    /// </para>
    /// </summary>
    internal static bool IsRefSpecifier(OwnershipSpecifierNode? specifier)
    {
        return specifier is RefSpecifierOwnershipSpecifierNode or ReadonlyRefSpecifierOwnershipSpecifierNode;
    }
}