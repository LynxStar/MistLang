using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
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

    /// <summary>
    /// The user indexer the access resolved to, or null for an element access the target supplies --
    /// an array's or a span's, whose intrinsic indexer has no accessor a rule could judge.
    /// </summary>
    public Indexer? Indexer { get; init; }

    public override IMistType Type { get; } = type;
}