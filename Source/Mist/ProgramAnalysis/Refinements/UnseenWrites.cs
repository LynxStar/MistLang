using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// Captured writes, ref/out arguments and ref-local aliases can invalidate facts outside this body's flow.
/// Proofs have no runtime guard, so these holders and aliases retain only declared types, which still
/// constrain every write.
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
                    AddByRef(unseen, call.Arguments, call.Parameters);
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

        var targets = inside.OfType<AssignmentExpression>().Select(assignment => assignment.Target)
            .Concat(inside.OfType<CompoundAssignmentExpression>().Select(compound => compound.Target));

        foreach(var written in targets.Select(Referenced).OfType<object>())
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