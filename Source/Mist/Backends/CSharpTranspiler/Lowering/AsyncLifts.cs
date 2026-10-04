using Mist.ProgramAnalysis;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.Backends.CSharpTranspiler.Lowering;

internal static class AsyncLifts
{
    public static bool NeedsAwait(Expression expression)
    {
        var pending = new Stack<object>();
        pending.Push(expression);

        while(pending.Count > 0)
        {
            var node = pending.Pop();

            if(node is InvocationExpression invocation
                && (AsyncPromotion.IsPromoted(invocation.Method) || TaskResult.Dropped(invocation) is not null))
            {
                return true;
            }

            if(node is LambdaExpression or AsyncExpression { IsLift: true } or AsyncBlockExpression)
            {
                continue;
            }

            foreach(var child in BodyWalker.ChildrenOf(node))
            {
                pending.Push(child);
            }
        }

        return false;
    }
}
