using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// The holders of one body that can be written where its flow does not look: assigned inside a closure
/// that captured them, which runs whenever it is called; handed to a `ref` or `out` parameter; or aliased
/// by a ref local, the alias included. What the flow learned of such a holder may be stale at any later
/// step, and there is no runtime guard behind a proof, so the flow keeps nothing of them and each is
/// known only by its declared type -- which every write, wherever it sits, is still judged against.
/// </summary>
internal static class UnseenWrites
{
    public static readonly IReadOnlySet<object> None = new HashSet<object>();

    public static IReadOnlySet<object> Of(object root)
    {
        var unseen = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach(var node in BodyWalker.DescendantsAndSelf(root))
        {
            switch(node)
            {
                case LambdaExpression lambda:
                    AddCapturedWrites(unseen, lambda.Parameters, Captures.Inside(lambda));
                    break;

                case LocalFunctionStatement function:
                    AddCapturedWrites(unseen, function.Parameters, Captures.Inside(function));
                    break;

                case AsyncBlockExpression block:
                    AddCapturedWrites(unseen, [], BodyWalker.DescendantsAndSelf(block.Body).ToList());
                    break;

                case InvocationExpression call:
                    AddByRef(unseen, call.Arguments, call.Method?.ParametersList);
                    break;

                case NewExpression creation:
                    AddByRef(unseen, creation.Arguments, creation.Constructor?.ParametersList);
                    break;

                case LocalDeclarationStatement { Local: { IsRef: true } alias, Initializer: var aliased }:
                    _ = unseen.Add(alias);

                    if(Referenced(aliased) is { } target)
                    {
                        _ = unseen.Add(target);
                    }

                    break;
            }
        }

        return unseen;
    }

    private static void AddCapturedWrites(HashSet<object> unseen, IEnumerable<LocalVariable> parameters, IReadOnlyList<object> inside)
    {
        var own = Captures.OwnNames(parameters, inside);

        foreach(var written in inside.OfType<AssignmentExpression>().Select(assignment => Referenced(assignment.Target)).OfType<object>())
        {
            if(written is not LocalVariable local || !own.Contains(local))
            {
                _ = unseen.Add(written);
            }
        }
    }

    private static object? Referenced(Expression? expression)
    {
        return expression switch
        {
            LocalReferenceExpression local => local.Local,
            FieldReferenceExpression field => field.Field,
            MemberAccessExpression { Member: FieldResult field } => field.Field,
            _ => null,
        };
    }

    private static void AddByRef(HashSet<object> unseen, IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters)
    {
        for(var i = 0; parameters is not null && i < arguments.Count && i < parameters.Count; i++)
        {
            if(parameters[i].Modifier.Direction is ByRefDirection.Ref or ByRefDirection.Out && Referenced(arguments[i]) is { } passed)
            {
                _ = unseen.Add(passed);
            }
        }
    }
}