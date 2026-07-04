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

        Block? body = null;

        if(methodDecl.Body is CallableBlockBodyCallableBodyNode blockBody)
        {
            body = new Block(blockBody.Block);
        }

        if(modifiers.IsIntrinsic)
        {
            ValidateIntrinsic(methodDecl, memberType, name, body);
        }
        else if(body is null && memberType is not AbstractType)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.MethodMustHaveBody, methodDecl, null, name);
        }

        var method = new Method(name, returnType, parameters, modifiers)
        {
            Body = body,
            ReturnOwnership = returnOwnership,
        };

        memberType.MethodsList.Add(method);
    }

    private void ValidateIntrinsic(MethodDeclarationNode methodDecl, MemberType memberType, string name, Block? body)
    {
        if(body is not null)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicMemberCannotHaveBody, methodDecl, null, name);
        }

        var typeIsIntrinsic = memberType is ConcreteType concrete && concrete.IsIntrinsic;

        if(!typeIsIntrinsic)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicRequiresIntrinsicType, methodDecl, null, memberType.Name);
        }
    }
}