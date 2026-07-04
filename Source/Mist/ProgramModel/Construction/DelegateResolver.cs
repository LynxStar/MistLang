using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Resolves the shared portions of any delegate (callable) declaration and
/// runs the standard intrinsic-or-body validation. This is the single point
/// of truth for resolving modifiers, parameters, body, and validating the
/// intrinsic/body contract.
/// </summary>
internal sealed class DelegateResolver
{
    private readonly BuilderContext context;
    private readonly ParameterBuilder parameterBuilder;

    public DelegateResolver(BuilderContext context, ParameterBuilder parameterBuilder)
    {
        this.context = context;
        this.parameterBuilder = parameterBuilder;
    }

    /// <summary>
    /// Resolves the 4 common pieces from any <see cref="IDelegateNode"/> and
    /// validates the intrinsic-or-body contract.
    /// </summary>
    public ResolvedDelegate ResolveDelegate<TNode>(TNode node, Scope scope, MemberType memberType, string memberName)
        where TNode : SyntaxNode, IDelegateNode
    {
        var modifiers = ModifierBuilder.Build(node, context.Diagnostics);
        var parameters = parameterBuilder.Resolve(node.Parameters, scope);
        var body = ResolveBody(node.Body);

        ValidateIntrinsicOrBody(node, memberType, memberName, modifiers, body);

        return new ResolvedDelegate(modifiers, parameters, body);
    }

    /// <summary>
    /// Resolves the 6 pieces from a <see cref="IFullDelegateNode"/> and
    /// validates the intrinsic-or-body contract.
    /// </summary>
    public ResolvedFullDelegate ResolveFullDelegate<TNode>(TNode node, Scope scope, MemberType memberType, string memberName)
        where TNode : SyntaxNode, IFullDelegateNode
    {
        var @base = ResolveDelegate(node, scope, memberType, memberName);

        // WithClauses and Constraints are parsed but not yet resolved into model types.
        // That will be added when we implement with/where resolution.

        return new ResolvedFullDelegate(@base);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Validation
    // ═══════════════════════════════════════════════════════════════════════

    private static Block? ResolveBody(CallableBodyNode bodyNode)
    {
        if(bodyNode is CallableBlockBodyCallableBodyNode blockBody)
        {
            return new Block(blockBody.Block);
        }

        return null;
    }

    private void ValidateIntrinsicOrBody(
        SyntaxNode locationNode,
        MemberType memberType,
        string memberName,
        MemberModifiers modifiers,
        Block? body)
    {
        if(modifiers.IsIntrinsic)
        {
            ValidateIntrinsic(locationNode, memberType, memberName, body);
        }
        else
        {
            ValidateBodyRequired(locationNode, memberType, memberName, body);
        }
    }

    private void ValidateIntrinsic(
        SyntaxNode locationNode,
        MemberType memberType,
        string memberName,
        Block? body)
    {
        if(memberType is AbstractType)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicNotValidOnMember, locationNode, null, "abstract members");
            return;
        }

        if(body is not null)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicMemberCannotHaveBody, locationNode, null, memberName);
        }

        var typeIsIntrinsic = memberType is ConcreteType concrete && concrete.IsIntrinsic;

        if(!typeIsIntrinsic)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicRequiresIntrinsicType, locationNode, null, memberType.Name);
        }
    }

    private void ValidateBodyRequired(
        SyntaxNode locationNode,
        MemberType memberType,
        string memberName,
        Block? body)
    {
        if(body is null && memberType is not AbstractType)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.MethodMustHaveBody, locationNode, null, memberName);
        }
    }
}