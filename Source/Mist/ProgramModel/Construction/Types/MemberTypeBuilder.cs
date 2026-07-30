using Mist.Common.Diagnostics;
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
        ParameterBuilder = new ParameterBuilder(scopeResolver);
        DelegateResolver = new DelegateResolver(context, ParameterBuilder, scopeResolver);
        PropertyBuilder = new PropertyBuilder(context, scopeResolver, accessorBuilder);
        IndexerBuilder = new IndexerBuilder(context, scopeResolver, accessorBuilder, ParameterBuilder);
        MethodBuilder = new MethodBuilder(context, scopeResolver, DelegateResolver, ConstraintResolver);
        OperatorBuilder = new OperatorBuilder(context, scopeResolver, DelegateResolver);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Entry points
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Records the declaring type as its assembly's entry point if the method just
    /// resolved has the shape of one, reporting a diagnostic when the assembly already
    /// had one.
    /// <para>
    /// Called after each method is resolved, so the duplicate case is caught by the second
    /// assignment rather than by counting candidates once the whole program is built. Which
    /// of the two is left recorded does not matter: the diagnostic is an error and the
    /// pipeline stops before emission.
    /// </para>
    /// </summary>
    protected void RecordEntryPointIfPresent(MethodDeclarationNode methodDecl, MemberType typeDef)
    {
        var method = typeDef.MethodsList.LastOrDefault();

        if(method is null || !IsEntryPoint(method))
        {
            return;
        }

        var assembly = typeDef.Assembly;

        if(assembly.EntryPoint is not null)
        {
            Context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.DuplicateEntryPoint, methodDecl, null,
                typeDef.FullyQualifiedName, assembly.EntryPoint.FullyQualifiedName);
            return;
        }

        assembly.EntryPoint = typeDef;
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
            _ => OwnershipKind.Unrestricted
        };
    }
}