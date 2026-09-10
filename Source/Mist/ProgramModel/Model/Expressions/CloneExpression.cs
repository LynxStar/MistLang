using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// An INSERTED clone, standing where a non-owned value reaches an owned position. Modelled rather than
/// left to the emitter because the copy is observable: the two instances diverge from here on, and a
/// program that mutates one and reads the other says so in its output.
/// </summary>
internal sealed class CloneExpression(SyntaxNode node, Expression source) : Expression(node)
{
    public Expression Source { get; } = source;

    public override IMistType Type { get; } = source.Type;
}