using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// An index expression (<c>receiver[index]</c>).
/// </summary>
internal sealed class IndexExpression(
    SyntaxNode node,
    Expression receiver,
    Expression index,
    IMistType type) : Expression(node)
{
    public Expression Receiver { get; } = receiver;

    public Expression Index { get; } = index;

    public override IMistType Type { get; } = type;
}