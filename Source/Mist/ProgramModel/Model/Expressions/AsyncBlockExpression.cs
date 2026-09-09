using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Statements;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A braced body lifted into a task: <c>async { ... }</c>, or <c>async valuetask { ... }</c> where the
/// hint names which task. The block's own <c>return</c> statements say what the task produces, exactly
/// as a lambda's do -- this is the same shape reached through different syntax, and it borrows the
/// same inference.
/// </summary>
internal sealed class AsyncBlockExpression(
    SyntaxNode node, Block body, IMistType type, bool isValueTask) : Expression(node)
{
    public Block Body { get; } = body;

    /// <summary>
    /// Whether the hint asked for a value task. The two differ in what is constructed and in nothing
    /// else, the block being identical either way.
    /// </summary>
    public bool IsValueTask { get; } = isValueTask;

    public override IMistType Type { get; } = type;
}