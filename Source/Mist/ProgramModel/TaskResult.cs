using Mist.ProgramModel.Contracts.Types;
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

    private static bool IsTaskDefinition(ConstructedType constructed)
    {
        var name = constructed.Definition.FullyQualifiedName;

        return string.Equals(name, WellKnownTypes.TaskGeneric.FullyQualifiedName, StringComparison.Ordinal)
            || string.Equals(name, WellKnownTypes.ValueTaskGeneric.FullyQualifiedName, StringComparison.Ordinal)
            ;
    }
}