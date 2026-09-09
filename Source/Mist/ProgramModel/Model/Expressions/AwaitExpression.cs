using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// Awaiting a task, whose <see cref="Expression.Type"/> is the task's result. Covers both the
/// <c>await</c> an author writes in an async context and the one the binder INSERTS in a sync context,
/// because the two mean the same thing and differ only in who asked for them: modelling the implicit
/// one as anything else would leave the emitter deciding when a call is awaited.
/// </summary>
internal sealed class AwaitExpression(SyntaxNode node, Expression operand, IMistType type) : Expression(node)
{
    public Expression Operand { get; } = operand;

    /// <summary>
    /// Whether the binder supplied this rather than the author writing it. Carried for diagnosis and
    /// for tests; emission treats the two identically.
    /// </summary>
    public bool IsImplicit { get; init; }

    public override IMistType Type { get; } = type;
}