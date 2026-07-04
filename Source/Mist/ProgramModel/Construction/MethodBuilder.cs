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

    public MethodBuilder(BuilderContext context, TypeReferenceResolver typeResolver, DelegateResolver delegateResolver)
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
        var common = delegateResolver.ResolveFullDelegate(methodDecl, memberType.Scope, memberType, name);
        var returnType = typeResolver.Resolve(methodDecl.ReturnType, memberType.Scope);
        var returnOwnership = MemberTypeBuilder.ResolveOwnership(methodDecl.ReturnOwnership);

        var method = new Method(name, returnType, common.Parameters, common.Modifiers)
        {
            Body = common.Body,
            ReturnOwnership = returnOwnership,
        };

        memberType.MethodsList.Add(method);
    }
}
