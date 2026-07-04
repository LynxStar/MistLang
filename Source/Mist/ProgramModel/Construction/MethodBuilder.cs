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
    private readonly BuilderContext context;
    private readonly TypeReferenceResolver typeResolver;
    private readonly ParameterBuilder parameterBuilder;

    public MethodBuilder(BuilderContext context, TypeReferenceResolver typeResolver, ParameterBuilder parameterBuilder)
    {
        this.context = context;
        this.typeResolver = typeResolver;
        this.parameterBuilder = parameterBuilder;
    }

    /// <summary>
    /// Resolves a method declaration on any member-bearing type.
    /// </summary>
    public void Resolve(MethodDeclarationNode methodDecl, MemberType memberType)
    {
        var name = methodDecl.Name.Lexeme;
        var returnType = typeResolver.Resolve(methodDecl.ReturnType, memberType.Scope);
        var modifiers = ModifierBuilder.Build(methodDecl.Modifiers, context.Diagnostics, methodDecl);
        var parameters = parameterBuilder.Resolve(methodDecl.Parameters, memberType.Scope);
        var returnOwnership = MemberTypeBuilder.ResolveOwnership(methodDecl.ReturnOwnership);
        var body = CallableValidation.ResolveBody(methodDecl.Body);

        CallableValidation.ValidateIntrinsicOrBody(methodDecl, memberType, name, modifiers, body, context.Diagnostics);

        var method = new Method(name, returnType, parameters, modifiers)
        {
            Body = body,
            ReturnOwnership = returnOwnership,
        };

        memberType.MethodsList.Add(method);
    }
}