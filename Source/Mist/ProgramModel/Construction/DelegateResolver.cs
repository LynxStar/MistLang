using System.Collections.Immutable;
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
    private readonly TypeReferenceResolver typeResolver;

    public DelegateResolver(BuilderContext context, ParameterBuilder parameterBuilder, TypeReferenceResolver typeResolver)
    {
        this.context = context;
        this.parameterBuilder = parameterBuilder;
        this.typeResolver = typeResolver;
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
    /// Resolves the 6 pieces from a <see cref="IFullDelegateNode"/>: the base delegate
    /// plus context bindings and optional this-tier declaration from with clauses.
    /// </summary>
    public ResolvedFullDelegate ResolveFullDelegate<TNode>(TNode node, Scope scope, MemberType memberType, string memberName)
        where TNode : SyntaxNode, IFullDelegateNode
    {
        var baseDelegate = ResolveDelegate(node, scope, memberType, memberName);
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
                    var resolvedType = typeResolver.Resolve(contextClause.Type, scope);
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