using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
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
/// Resolves the shared portions of any delegate (callable) declaration and
/// runs the standard intrinsic-or-body validation. This is the single point
/// of truth for resolving modifiers, parameters, body, and validating the
/// intrinsic/body contract.
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

    /// <summary>
    /// Resolves the 4 common pieces from any <see cref="IDelegateNode"/> and
    /// validates the intrinsic-or-body contract.
    /// </summary>
    public ResolvedDelegate ResolveDelegate<TNode>(TNode node, Scope scope, IMethodHost host, string memberName)
        where TNode : SyntaxNode, IDelegateNode
    {
        var modifiers = ModifierBuilder.ForMember(node, context.Diagnostics, host as MistType);

        var parameters = parameterBuilder.Resolve(node.Parameters, scope);
        var body = ResolveBody(node.Body);

        ValidateIntrinsicOrBody(node, host, memberName, modifiers, body);

        return new ResolvedDelegate(modifiers, parameters, body);
    }

    /// <summary>
    /// Resolves the 6 pieces from a <see cref="IFullDelegateNode"/>: the base delegate
    /// plus context bindings and optional this-tier declaration from with clauses.
    /// </summary>
    public ResolvedFullDelegate ResolveFullDelegate<TNode>(TNode node, Scope scope, IMethodHost host, string memberName)
        where TNode : SyntaxNode, IFullDelegateNode
    {
        var baseDelegate = ResolveDelegate(node, scope, host, memberName);
        var (contextBindings, thisTier) = ResolveWithClauses(node.WithClauses, scope);

        return new ResolvedFullDelegate(baseDelegate, contextBindings, thisTier);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // With Clause Resolution
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves all with clauses on a full delegate into context bindings and
    /// an optional this-tier declaration.
    /// </summary>
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
                    contextBindings.Add(new ContextBinding(resolvedType, contextClause.Name.Lexeme));
                    break;

                default:
                    throw new InvalidOperationException($"Unrecognized with clause node: {clause.GetType().Name}");
            }
        }

        return (contextBindings, thisTier);
    }

    /// <summary>
    /// Maps an <see cref="OwnershipSpecifierNode"/> to the corresponding <see cref="ThisTier"/>.
    /// </summary>
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
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicNotValidOnMember, locationNode, null, "abstract members");
            return;
        }

        if(body is not null)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicMemberCannotHaveBody, locationNode, null, memberName);
        }

        if(!host.IsIntrinsic)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicRequiresIntrinsicType, locationNode, null, host.Name);
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
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.MethodMustHaveBody, locationNode, null, memberName);
        }
    }
}