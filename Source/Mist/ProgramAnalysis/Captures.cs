using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Names declared inside a closure, including nested closures' parameters, are not captures
/// of the enclosing body.
/// </summary>
internal static class Captures
{
    public static IReadOnlyList<object> Inside(LambdaExpression lambda)
    {
        var body = (object?)lambda.Body ?? lambda.Result;

        return body is null ? [] : BodyWalker.DescendantsAndSelf(body).ToList();
    }

    public static IReadOnlyList<object> Inside(LocalFunctionStatement function)
    {
        return BodyWalker.DescendantsAndSelf(function.Body).ToList();
    }

    public static (HashSet<LocalVariable> Own, IReadOnlyList<object> Inside)? Of(object closure)
    {
        var (parameters, inside) = closure switch
        {
            LambdaExpression { IsStatic: false } lambda => (lambda.Parameters, Inside(lambda)),
            LocalFunctionStatement { Function.Modifiers.IsStatic: false } function => (function.Parameters, Inside(function)),
            AsyncBlockExpression block => ([], BodyWalker.DescendantsAndSelf(block.Body).ToList()),
            _ => (null, []),
        };

        return parameters is null ? null : (OwnNames(parameters, inside), inside);
    }

    public static HashSet<LocalVariable> Captured(IEnumerable<object> nodes)
    {
        var captured = new HashSet<LocalVariable>(ReferenceEqualityComparer.Instance);

        foreach(var closure in nodes)
        {
            if(Of(closure) is var (own, inside))
            {
                captured.UnionWith(inside.OfType<LocalReferenceExpression>().Select(reference => reference.Local).Where(local => !own.Contains(local)));
            }
        }

        return captured;
    }

    public static HashSet<LocalVariable> OwnNames(IEnumerable<LocalVariable> parameters, IReadOnlyList<object> inside)
    {
        var own = new HashSet<LocalVariable>(parameters, ReferenceEqualityComparer.Instance);

        foreach(var node in inside)
        {
            switch(node)
            {
                case LocalDeclarationStatement declaration:
                    _ = own.Add(declaration.Local);
                    break;
                case ForEachStatement loop:
                    _ = own.Add(loop.Variable);
                    break;
                case TupleDeconstructionStatement deconstruction:
                    own.UnionWith(deconstruction.Targets);
                    break;
                case LambdaExpression nested:
                    own.UnionWith(nested.Parameters);
                    break;
                case LocalFunctionStatement nested:
                    own.UnionWith(nested.Parameters);
                    break;
                case WithBlockStatement { Binding: { } binding }:
                    _ = own.Add(binding);
                    break;
                case Pattern { Binding: { } binding }:
                    _ = own.Add(binding);
                    break;
            }
        }

        return own;
    }
}