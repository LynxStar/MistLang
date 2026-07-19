using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Resolves method declarations into <see cref="Method"/> instances.
/// A single code path handles both concrete and abstract contexts.
/// </summary>
internal sealed class MethodBuilder
{
    private readonly TypeReferenceResolver typeResolver;
    private readonly DelegateResolver delegateResolver;
    private readonly ConstraintResolver constraintResolver;
    private readonly DiagnosticBag diagnostics;

    public MethodBuilder(
        TypeReferenceResolver typeResolver,
        DelegateResolver delegateResolver,
        ConstraintResolver constraintResolver,
        DiagnosticBag diagnostics)
    {
        this.typeResolver = typeResolver;
        this.delegateResolver = delegateResolver;
        this.constraintResolver = constraintResolver;
        this.diagnostics = diagnostics;
    }

    /// <summary>
    /// Resolves a method declaration on any member-bearing type.
    /// </summary>
    public void Resolve(MethodDeclarationNode methodDecl, IMethodHost host)
    {
        var name = methodDecl.Name.Lexeme;

        // Every method gets its own scope (child of the type's scope).
        // Type parameters are registered here if present; later, locals will be too.
        var methodScope = new Scope(host.Scope)
        {
            Namespace = host.NamespaceName
        };

        var method = new Method(name, ErrorType.Instance, [], default);

        if(methodDecl.TypeParams is not null)
        {
            // A method declares its own type-parameter pack independent of the enclosing
            // type (Requirement 12.1); the shared populator classifies it with the same
            // rules used for every other declaration kind. Passing the diagnostic bag
            // enforces the at-most-one-Unbracketed_Pack rule for method type-parameter
            // lists, emitting MIST_VAR_0001 for a second params pack (Requirements 12.4, 12.5).
            TypeParameterPopulator.Populate(
                method.TypeParametersList, methodScope, host as MistType, methodDecl.TypeParams, diagnostics);
        }

        var common = delegateResolver.ResolveFullDelegate(methodDecl, methodScope, host, name);
        var returnType = typeResolver.Resolve(methodDecl.ReturnType, methodScope);
        var returnOwnership = MemberTypeBuilder.ResolveOwnership(methodDecl.ReturnOwnership);

        var resolvedMethod = new Method(name, returnType, common.Parameters, common.Modifiers)
        {
            Body = common.Body,
            ReturnOwnership = returnOwnership,
            Scope = methodScope,
        };

        // Transfer type parameters to the final method instance.
        resolvedMethod.TypeParametersList.AddRange(method.TypeParametersList);

        // Resolve constraints on method-level type parameters and parameter refinements.
        constraintResolver.Resolve(methodDecl.Constraints, resolvedMethod.TypeParametersList, resolvedMethod.ParametersList, methodScope);

        // Transfer with clause data.
        resolvedMethod.ContextBindingsList.AddRange(common.ContextBindings);
        resolvedMethod.DeclaredThisTier = common.DeclaredThisTier;

        host.MethodsList.Add(resolvedMethod);
    }
}