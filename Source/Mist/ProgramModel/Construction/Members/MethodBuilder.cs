using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Members;

/// <summary>
/// Resolves method declarations into <see cref="Method"/> instances.
/// A single code path handles both concrete and abstract contexts.
/// </summary>
internal sealed class MethodBuilder
{
    private readonly BuilderContext context;
    private readonly ScopeResolver scopeResolver;
    private readonly DelegateResolver delegateResolver;
    private readonly ConstraintResolver constraintResolver;

    public MethodBuilder(
        BuilderContext context,
        ScopeResolver scopeResolver,
        DelegateResolver delegateResolver,
        ConstraintResolver constraintResolver)
    {
        this.context = context;
        this.scopeResolver = scopeResolver;
        this.delegateResolver = delegateResolver;
        this.constraintResolver = constraintResolver;
    }

    /// <summary>
    /// Resolves a method declaration on any member-bearing type.
    /// </summary>
    public void Resolve(MethodDeclarationNode methodDecl, IMethodHost host)
    {
        var name = methodDecl.Name.Lexeme;

        MemberNameValidation.Validate(name, host, methodDecl, context.Diagnostics);

        // Every method gets its own scope (child of the host's scope), holding its type
        // parameters. Its staticness lives here too: it is the single point at which a lookup
        // leaving the member for the declaring type loses access to instance members.
        var methodScope = new MethodScope(host.Scope);

        var method = new Method(name, ErrorType.Instance, [], default);

        if(methodDecl.TypeParams is not null)
        {
            TypeParameterPopulator.Populate(
                method, methodScope, host as MistType, methodDecl.TypeParams, context.Diagnostics,
                context.Domains);
        }

        var common = delegateResolver.ResolveFullDelegate(methodDecl, methodScope, host, name);
        methodScope.IsStatic = common.Modifiers.IsStatic;

        // `async` is a RETURN-TYPE TRANSFORM rather than a marker on the body: `async int` declares a
        // method producing Task<int>, and every caller sees that type. The body still returns the
        // int -- see BodyBinder.BindDelegate, which unwraps this again for the body's own target.
        var declaredReturn = scopeResolver.Resolve(methodDecl.ReturnType, methodScope);
        var returnType = common.Modifiers.IsAsync
            ? scopeResolver.ConstructTask(declaredReturn)
            : declaredReturn
            ;
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

        context.Attributes.Build(methodDecl.Attributes, resolvedMethod.DeclaredAttributesList, host.Scope);
        host.MethodsList.Add(resolvedMethod);
    }
}