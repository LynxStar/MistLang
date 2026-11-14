using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Flow;

internal enum CallEntry
{
    Certain,
    Conditional,
    Absentable,
}

/// <summary>
/// Arguments remain the caller's if a conditional call skips an absent receiver or a null receiver
/// throws before entry. Static calls, extension carriers, and calls on this or a fresh value always enter.
/// </summary>
internal static class CallEntries
{
    public static CallEntry Of(InvocationExpression invocation, Func<Expression, bool> holds)
    {
        return invocation switch
        {
            { Receiver: MemberAccessExpression { IsConditional: true } } => CallEntry.Conditional,
            { Receiver: MemberAccessExpression { Receiver: var receiver }, Method: { Modifiers.IsStatic: false, DeclaringBlock: null } }
                when receiver is not (ThisExpression or NewExpression or TypeReferenceExpression or LiteralExpression)
                    && !holds(receiver)
                    && PurityJudge.DefinitionOf(receiver.Type) is ClassType or AbstractType => CallEntry.Absentable,
            _ => CallEntry.Certain,
        };
    }
}