using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model;

/// <summary>
/// Records information about a discarded task expression within a block scope —
/// a bare async method call whose <c>Task&lt;T&gt;</c> result is not assigned to a
/// variable, awaited, or returned. The emitter uses this information to capture
/// the task into a synthetic variable and emit scope-exit await code
/// (e.g., <c>await __task_0;</c> or <c>await Task.WhenAll(...)</c>).
///
/// Named task locals are no longer tracked here — they are scope-exit drop
/// obligations handled by the emitter's auto-drop try/finally wrapping.
/// </summary>
public sealed class DiscardedTaskInfo
{
    public DiscardedTaskInfo(
        string generatedVariableName,
        IType taskType,
        Storm.Runtime.SyntaxNode originNode)
    {
        GeneratedVariableName = generatedVariableName;
        TaskType = taskType;
        OriginNode = originNode;
    }

    /// <summary>
    /// The synthetic variable name assigned to this task for emission
    /// (e.g., <c>__task_0</c>, <c>__task_1</c>).
    /// </summary>
    public string GeneratedVariableName { get; }

    /// <summary>
    /// The <c>Task&lt;T&gt;</c> type of this task.
    /// </summary>
    public IType TaskType { get; }

    /// <summary>
    /// The AST node (invocation expression) that created this task.
    /// </summary>
    public Storm.Runtime.SyntaxNode OriginNode { get; }
}
