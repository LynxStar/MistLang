// Backward-compatible helpers for the ForeachStatementNode alternation.
//
// The grammar changed from:
//   ForeachStatement = FOREACH ( type:TypeReference name IN collection ) body
// to:
//   ForeachStatement = ... as TypedForeach | ... as VarForeach
//
// These helpers provide uniform access to the common fields (Name, Collection, Body)
// and optional access to the Type (null for VarForeach).

using Storm.Runtime;

namespace Mist.Generated;

/// <summary>
/// Dispatch helpers on the abstract <see cref="ForeachStatementNode"/> introduced when
/// the <c>ForeachStatement</c> grammar rule was split into typed and var forms.
/// </summary>
public static class ForeachStatementNodeExtensions
{
    /// <summary>
    /// Returns the explicit type reference, or <c>null</c> for a <c>var</c> foreach.
    /// </summary>
    public static TypeReferenceNode? GetTypeOrNull(this ForeachStatementNode node)
    {
        return node switch
        {
            TypedForeachForeachStatementNode typed => typed.Type,
            _ => null,
        };
    }

    /// <summary>
    /// Returns the iteration variable name token.
    /// </summary>
    public static Token GetName(this ForeachStatementNode node)
    {
        return node switch
        {
            TypedForeachForeachStatementNode typed => typed.Name,
            VarForeachForeachStatementNode varForeach => varForeach.Name,
            _ => throw new InvalidOperationException($"Unknown ForeachStatement variant: {node.GetType().Name}"),
        };
    }

    /// <summary>
    /// Returns the collection expression.
    /// </summary>
    public static ExpressionNode GetCollection(this ForeachStatementNode node)
    {
        return node switch
        {
            TypedForeachForeachStatementNode typed => typed.Collection,
            VarForeachForeachStatementNode varForeach => varForeach.Collection,
            _ => throw new InvalidOperationException($"Unknown ForeachStatement variant: {node.GetType().Name}"),
        };
    }

    /// <summary>
    /// Returns the loop body block.
    /// </summary>
    public static BlockNode GetBody(this ForeachStatementNode node)
    {
        return node switch
        {
            TypedForeachForeachStatementNode typed => typed.Body,
            VarForeachForeachStatementNode varForeach => varForeach.Body,
            _ => throw new InvalidOperationException($"Unknown ForeachStatement variant: {node.GetType().Name}"),
        };
    }

    /// <summary>
    /// Returns whether this is a <c>var</c> foreach (type-inferred).
    /// </summary>
    public static bool IsVar(this ForeachStatementNode node)
    {
        return node is VarForeachForeachStatementNode;
    }
}