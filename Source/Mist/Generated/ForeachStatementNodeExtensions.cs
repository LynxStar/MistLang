using Storm.Runtime;

namespace Mist.Generated;

/// <summary>
/// <see cref="ForeachStatementNode"/> has a typed and a <c>var</c> form; these read its fields without
/// switching on the form.
/// </summary>
public static class ForeachStatementNodeExtensions
{
    public static TypeReferenceNode? GetTypeOrNull(this ForeachStatementNode node)
    {
        return node switch
        {
            TypedForeachForeachStatementNode typed => typed.Type,
            _ => null,
        };
    }

    public static Token GetName(this ForeachStatementNode node)
    {
        return node switch
        {
            TypedForeachForeachStatementNode typed => typed.Name,
            VarForeachForeachStatementNode varForeach => varForeach.Name,
            _ => throw new InvalidOperationException($"Unknown ForeachStatement variant: {node.GetType().Name}"),
        };
    }

    public static ExpressionNode GetCollection(this ForeachStatementNode node)
    {
        return node switch
        {
            TypedForeachForeachStatementNode typed => typed.Collection,
            VarForeachForeachStatementNode varForeach => varForeach.Collection,
            _ => throw new InvalidOperationException($"Unknown ForeachStatement variant: {node.GetType().Name}"),
        };
    }

    public static BlockNode GetBody(this ForeachStatementNode node)
    {
        return node switch
        {
            TypedForeachForeachStatementNode typed => typed.Body,
            VarForeachForeachStatementNode varForeach => varForeach.Body,
            _ => throw new InvalidOperationException($"Unknown ForeachStatement variant: {node.GetType().Name}"),
        };
    }

    public static bool IsVar(this ForeachStatementNode node)
    {
        return node is VarForeachForeachStatementNode;
    }
}