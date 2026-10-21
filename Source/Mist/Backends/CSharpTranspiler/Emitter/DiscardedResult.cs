using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// A value written where its result is thrown away, as the body of a lambda whose result is void
/// (first-class-functions Requirement 3.4). C# admits only an assignment, a call, an await, an increment or
/// a construction as a statement, so anything else is assigned to the discard. A call keeps its bare form,
/// a void one having no value to assign, unless the backend respells it into something that is no call.
/// </summary>
internal static class DiscardedResult
{
    public static string? Lead(Expression result)
    {
        return result switch
        {
            AssignmentExpression => null,
            InvocationExpression call when OptionalUnion.IsVoid(call.Type) || !IntrinsicCalls.Respells(call.Method) => null,
            _ => "_ = ",
        };
    }
}