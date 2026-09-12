using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The Standard task types spelled as the CLR's own, which is the only spelling <c>await</c> and every
/// framework API accept -- a structurally identical duplicate would satisfy neither.
/// <para>
/// THE VOID CASE COLLAPSES TO THE NON-GENERIC TASK. Mist models "a task with no result" as
/// <c>Task&lt;void&gt;</c>, so one declaration covers both and <c>async void</c> needs no second rule;
/// C# splits them into two types instead, and <c>Task&lt;Void&gt;</c> is not one of them.
/// </para>
/// </summary>
internal static class TaskProjection
{
    /// <summary>
    /// The C# spelling of a task construction, or null when the definition is not one. The result type
    /// is emitted through the caller's own <paramref name="emit"/> so a nested construction takes the
    /// same route this one did.
    /// </summary>
    public static string? Emit(ConstructedType constructed, Func<IMistType, string> emit)
    {
        if(!TaskResult.IsTask(constructed) || ClrSpelling.Spelling(constructed.Definition) is not { } clrName)
        {
            return null;
        }

        var result = constructed.TypeArguments[0];

        return OptionalUnion.IsVoid(result) ? clrName : $"{clrName}<{emit(result)}>";
    }
}