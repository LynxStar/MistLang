using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramModel.Construction.Members;

/// <summary>
/// The one place every callable's modifiers, parameters and body are resolved and the
/// intrinsic-or-body contract is checked.
/// </summary>
internal sealed class DelegateResolver
{
    private readonly BuilderContext context;
    private readonly ParameterBuilder parameterBuilder;
    private readonly ScopeResolver scopeResolver;

    public DelegateResolver(BuilderContext context, ParameterBuilder parameterBuilder, ScopeResolver scopeResolver)
    {
        this.context = context;
        this.parameterBuilder = parameterBuilder;
        this.scopeResolver = scopeResolver;
    }

    public ResolvedDelegate ResolveDelegate<TNode>(TNode node, Scope scope, IMethodHost host, string memberName)
        where TNode : SyntaxNode, IDelegateNode
    {
        var modifiers = ModifierBuilder.ForMember(node, context.Diagnostics, host as MistType);

        var parameters = parameterBuilder.Resolve(node.Parameters, scope);
        var body = ResolveBody(node.Body);

        ValidateIntrinsicOrBody(node, host, memberName, modifiers, body);

        return new ResolvedDelegate(modifiers, parameters, body);
    }

    public ResolvedFullDelegate ResolveFullDelegate<TNode>(TNode node, Scope scope, IMethodHost host, string memberName)
        where TNode : SyntaxNode, IFullDelegateNode
    {
        var baseDelegate = ResolveDelegate(node, scope, host, memberName);
        var (contextBindings, thisTier) = ResolveWithClauses(node.WithClauses, scope);

        return new ResolvedFullDelegate(baseDelegate, contextBindings, thisTier);
    }

    private (List<ContextBinding> contextBindings, ThisTier? thisTier) ResolveWithClauses(
        ImmutableArray<WithClauseNode> withClauses,
        Scope scope)
    {
        var contextBindings = new List<ContextBinding>();
        ThisTier? thisTier = null;

        if(withClauses.IsDefaultOrEmpty)
        {
            return (contextBindings, thisTier);
        }

        foreach(var clause in withClauses)
        {
            switch(clause)
            {
                case ThisTierWithClauseWithClauseNode tierClause:
                    thisTier = MapOwnershipSpecifierToThisTier(tierClause.Tier);
                    break;

                case ContextWithClauseWithClauseNode contextClause:
                    var resolvedType = scopeResolver.Resolve(contextClause.Type, scope);
                    contextBindings.Add(new ContextBinding(resolvedType, contextClause.Name.Lexeme) { Node = contextClause });
                    break;

                default:
                    throw new InvalidOperationException($"Unrecognized with clause node: {clause.GetType().Name}");
            }
        }

        return (contextBindings, thisTier);
    }

    private static ThisTier MapOwnershipSpecifierToThisTier(OwnershipSpecifierNode specifier)
    {
        return specifier switch
        {
            OwnedSpecifierOwnershipSpecifierNode => ThisTier.Owned,
            BorrowedSpecifierOwnershipSpecifierNode => ThisTier.Borrowed,
            FrozenSpecifierOwnershipSpecifierNode => ThisTier.Frozen,
            ReadonlyRefSpecifierOwnershipSpecifierNode => ThisTier.ReadonlyRef,
            _ => throw new InvalidOperationException($"Unrecognized ownership specifier node: {specifier.GetType().Name}")
        };
    }

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
        IMethodHost host,
        string memberName,
        MemberModifiers modifiers,
        Block? body)
    {
        if(modifiers.IsIntrinsic)
        {
            ValidateIntrinsic(locationNode, host, memberName, body);
        }
        else
        {
            ValidateBodyRequired(locationNode, host, memberName, body);
        }
    }

    private void ValidateIntrinsic(
        SyntaxNode locationNode,
        IMethodHost host,
        string memberName,
        Block? body)
    {
        if(host.IsAbstract)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.IntrinsicNotValidOnMember, locationNode, null, new TextValue("abstract members"));
            return;
        }

        if(body is not null)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.IntrinsicMemberCannotHaveBody, locationNode, null, new NameValue(memberName));
        }

        if(!host.IsIntrinsic && !ClrCatch.Declares(host, memberName))
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.IntrinsicRequiresIntrinsicType, locationNode, null, new NameValue(host.Name));
        }
    }

    private void ValidateBodyRequired(
        SyntaxNode locationNode,
        IMethodHost host,
        string memberName,
        Block? body)
    {
        if(body is null && !host.IsAbstract)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.MethodMustHaveBody, locationNode, null, new NameValue(memberName));
        }
    }
}