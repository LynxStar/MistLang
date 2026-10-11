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
/// Whether a call is entered once its arguments are evaluated. A conditional one is skipped when its
/// receiver is absent, and an instance call through a reference that turns out null throws before the
/// callee begins; either way what it was handed is still the caller's. A static call, an extension's
/// carrier and a call on this or on a value just made always begin.
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