using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model;

/// <summary>
/// Records information about a "live" task within a block scope — a task that
/// has been created (via async method invocation or async functor) but has not
/// yet been explicitly awaited or returned. The emitter uses this information
/// to generate scope-exit await code (e.g., <c>await Task.WhenAll(...)</c>).
/// </summary>
public sealed class LiveTaskInfo
{
    public LiveTaskInfo(
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

    /// <summary>
    /// Set to <c>true</c> when an <c>await</c> expression is encountered
    /// that consumes this task. Consumed tasks are not included in the
    /// scope-exit await.
    /// </summary>
    public bool IsExplicitlyAwaited { get; set; }

    /// <summary>
    /// Set to <c>true</c> when this task is returned from the method.
    /// Returned tasks transfer ownership to the caller's scope and are
    /// not included in the scope-exit await.
    /// </summary>
    public bool IsReturned { get; set; }
}
