using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis;

internal static class LambdaCaptures
{
    public static IReadOnlyList<object> Inside(LambdaExpression lambda)
    {
        var body = (object?)lambda.Body ?? lambda.Result;

        return body is null ? [] : BodyWalker.DescendantsAndSelf(body).ToList();
    }

    public static HashSet<LocalVariable> OwnNames(LambdaExpression lambda, IReadOnlyList<object> inside)
    {
        var own = new HashSet<LocalVariable>(lambda.Parameters, ReferenceEqualityComparer.Instance);

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