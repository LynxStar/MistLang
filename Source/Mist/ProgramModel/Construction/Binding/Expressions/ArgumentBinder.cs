using Mist.Generated;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

/// <summary>
/// Binds an argument list. The single place every call-shaped construct goes through:
/// invocations and method calls, object creation, and constructor chains. Keeping them
/// on one path means argument-level concerns -- propagating a target type from the
/// selected overload's parameters, and honouring <c>ref</c> arguments, neither of which
/// is handled yet -- are added once rather than per call site.
/// </summary>
internal static class ArgumentBinder
{
    /// <summary>
    /// Binds each argument in <paramref name="argumentList"/> in order, or returns an
    /// empty list when the construct has no argument list at all (as in <c>f()</c>).
    /// </summary>
    internal static IReadOnlyList<Expression> Bind(ArgumentListNode? argumentList, BindingContext ctx, BuilderContext context)
    {
        if(argumentList is null)
        {
            return [];
        }

        var expressions = argumentList.GetAllExpressions();
        var bound = new List<Expression>(expressions.Length);
        foreach(var expression in expressions)
        {
            bound.Add(ExpressionBinder.BindExpression(expression, ctx, context));
        }

        return bound;
    }
}