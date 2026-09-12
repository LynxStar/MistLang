using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel;

/// <summary>
/// What a task type produces, shared by the binder and the backend. A task is EXACTLY a construction
/// of Standard's Task or ValueTask over one argument; anything else answers null, including a type
/// that merely resembles one.
/// </summary>
internal static class TaskResult
{
    public static IMistType? Of(IMistType type)
    {
        return type is ConstructedType { TypeArguments.Count: 1 } constructed && IsTaskDefinition(constructed)
            ? constructed.TypeArguments[0]
            : null
            ;
    }

    public static bool IsTask(IMistType type)
    {
        return Of(type) is not null;
    }

    /// <summary>
    /// The <c>Drop()</c> Standard declares on Task and ValueTask, which <c>await</c> is sugar for
    /// (drop-semantics Req 7.5): the binder binds an await to it, and a backend spells a call to it
    /// however its target completes a task.
    /// </summary>
    public static bool IsDrop(Method method)
    {
        return method is { Name: "Drop", ParametersList.Count: 0, Modifiers.IsIntrinsic: true, DeclaringType: { } declaring }
            && (string.Equals(declaring.FullyQualifiedName, WellKnownTypes.TaskGeneric.FullyQualifiedName, StringComparison.Ordinal)
                || string.Equals(declaring.FullyQualifiedName, WellKnownTypes.ValueTaskGeneric.FullyQualifiedName, StringComparison.Ordinal));
    }

    public static Expression? Dropped(Expression expression)
    {
        return expression is InvocationExpression { Method: { } method, Receiver: MemberAccessExpression access } && IsDrop(method)
            ? access.Receiver
            : null;
    }

    private static bool IsTaskDefinition(ConstructedType constructed)
    {
        var name = constructed.Definition.FullyQualifiedName;

        return string.Equals(name, WellKnownTypes.TaskGeneric.FullyQualifiedName, StringComparison.Ordinal)
            || string.Equals(name, WellKnownTypes.ValueTaskGeneric.FullyQualifiedName, StringComparison.Ordinal)
            ;
    }
}