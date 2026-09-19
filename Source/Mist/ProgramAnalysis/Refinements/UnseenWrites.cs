using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// The locals of one body that can be written where its flow does not look: assigned inside a closure
/// that captured them, which runs whenever it is called; handed to a `ref` or `out` parameter; or aliased
/// by a ref local, the alias included. What the flow learned of such a local may be stale at any later
/// step, and there is no runtime guard behind a proof, so the flow keeps nothing of them and each is
/// known only by its declared type -- which every write, wherever it sits, is still judged against.
/// </summary>
internal static class UnseenWrites
{
    public static readonly IReadOnlySet<LocalVariable> None = new HashSet<LocalVariable>();

    public static IReadOnlySet<LocalVariable> Of(object root)
    {
        var unseen = new HashSet<LocalVariable>(ReferenceEqualityComparer.Instance);

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

                    if(aliased is LocalReferenceExpression target)
                    {
                        _ = unseen.Add(target.Local);
                    }

                    break;
            }
        }

        return unseen;
    }

    private static void AddCapturedWrites(HashSet<LocalVariable> unseen, IEnumerable<LocalVariable> parameters, IReadOnlyList<object> inside)
    {
        var own = Captures.OwnNames(parameters, inside);

        foreach(var written in inside.OfType<AssignmentExpression>().Select(assignment => assignment.Target).OfType<LocalReferenceExpression>())
        {
            if(!own.Contains(written.Local))
            {
                _ = unseen.Add(written.Local);
            }
        }
    }

    private static void AddByRef(HashSet<LocalVariable> unseen, IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters)
    {
        for(var i = 0; parameters is not null && i < arguments.Count && i < parameters.Count; i++)
        {
            if(parameters[i].Modifier.Direction is ByRefDirection.Ref or ByRefDirection.Out && arguments[i] is LocalReferenceExpression passed)
            {
                _ = unseen.Add(passed.Local);
            }
        }
    }
}