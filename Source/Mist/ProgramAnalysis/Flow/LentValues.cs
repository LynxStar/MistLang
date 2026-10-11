using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Flow;

/// <summary>
/// A fresh linear value passed to a frozen or borrowed parameter. The callee only borrows it, as it does a
/// lease, so the statement lending it is its one holder and releases it once that statement is done -- a
/// declaration once the scope it declares into is; a loop condition or step lends afresh each time it runs
/// and releases the last one as the body begins.
/// </summary>
internal static class LentValues
{
    public static IEnumerable<(Expression Value, Parameter Parameter)> Of(object node)
    {
        var (arguments, parameters) = node switch
        {
            InvocationExpression invocation => (invocation.Arguments, invocation.Parameters),
            NewExpression creation => (creation.Arguments, creation.Constructor?.ParametersList),
            _ => ([], null),
        };

        for(var i = 0; i < arguments.Count && parameters is not null && i < parameters.Count; i++)
        {
            if(parameters[i].Modifier is { IsRef: false, Ownership: OwnershipKind.Frozen or OwnershipKind.Borrowed }
                && LocalLifetimes.IsTracked(arguments[i].Type)
                && arguments[i] is InvocationExpression or NewExpression or CloneExpression)
            {
                yield return (arguments[i], parameters[i]);
            }
        }
    }

    public static List<Expression> Once(Statement statement)
    {
        return In(Roots(statement));
    }

    public static List<Expression> Within(Expression root)
    {
        return In([root]);
    }

    public static List<Expression> Repeated(Statement statement)
    {
        return In(statement switch
        {
            WhileStatement loop => [loop.Condition],
            ForStatement loop => [.. new[] { loop.Condition, loop.Iterator }.OfType<Expression>()],
            _ => [],
        });
    }

    private static IReadOnlyList<Expression> Roots(Statement statement)
    {
        return statement switch
        {
            ExpressionStatement evaluated => [evaluated.Expression],
            LocalDeclarationStatement { Initializer: { } initializer } => [initializer],
            ReturnStatement { Value: { } returned } => [returned],
            TupleDeconstructionStatement deconstruction => [deconstruction.Initializer],
            ForEachStatement loop => [loop.Collection],
            WithBlockStatement scope => [scope.Value],
            IfStatement branch => Conditions(branch),
            ForStatement { Initializer: { } initializer } => Roots(initializer),
            _ => [],
        };
    }

    private static List<Expression> Conditions(IfStatement branch)
    {
        return branch.ElseBranch is IfStatement next ? [branch.Condition, .. Conditions(next)] : [branch.Condition];
    }

    private static List<Expression> In(IReadOnlyList<Expression> roots)
    {
        var lent = new List<Expression>();
        var pending = new Stack<object>(roots.Reverse());

        while(pending.Count > 0)
        {
            var node = pending.Pop();

            if(node is LambdaExpression or AsyncBlockExpression or Block)
            {
                continue;
            }

            foreach(var child in BodyWalker.ChildrenOf(node).Reverse())
            {
                pending.Push(child);
            }

            lent.AddRange(Of(node).Select(found => found.Value));
        }

        return lent;
    }
}