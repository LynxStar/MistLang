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

    public MethodBuilder(TypeReferenceResolver typeResolver, DelegateResolver delegateResolver)
    {
        this.typeResolver = typeResolver;
        this.delegateResolver = delegateResolver;
    }

    /// <summary>
    /// Resolves a method declaration on any member-bearing type.
    /// </summary>
    public void Resolve(MethodDeclarationNode methodDecl, MemberType memberType)
    {
        var name = methodDecl.Name.Lexeme;

        // Every method gets its own scope (child of the type's scope).
        // Type parameters are registered here if present; later, locals will be too.
        var methodScope = new Scope(memberType.Scope);
        methodScope.SetNamespace(memberType.NamespaceName);

        var method = new Method(name, ErrorType.Instance, [], default);

        if(methodDecl.TypeParams is not null)
        {
            TypeParameterPopulator.Populate(method.TypeParametersList, methodScope, memberType, methodDecl.TypeParams);
        }

        var common = delegateResolver.ResolveFullDelegate(methodDecl, methodScope, memberType, name);
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

        memberType.MethodsList.Add(resolvedMethod);
    }
}