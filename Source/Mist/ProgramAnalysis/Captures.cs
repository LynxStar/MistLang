using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis;

/// <summary>
/// What a closure -- a lambda or a local function -- declares for itself, so that every other local
/// it references is a capture of the enclosing body's.
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