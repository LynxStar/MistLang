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
    /// The user indexer the index selected among the receiver's, or null for an access the target
    /// supplies -- an array's or a span's, whose intrinsic indexer has no accessor a rule could judge --
    /// and for a slice <see cref="Slice"/> reaches.
    /// </summary>
    public Indexer? Indexer { get; init; }

    /// <summary>
    /// The type the index was bound against: the parameter of the indexer it selected, read through the
    /// receiver's type arguments, and recorded for an intrinsic indexer too, where <see cref="Indexer"/> is
    /// not. Null for a from-end index, which reaches the element through the receiver's Count or Length
    /// rather than through the parameter.
    /// </summary>
    public IMistType? ParameterType { get; init; }

    public CountedIndexer? FromEnd { get; init; }

    public CountedSlice? Slice { get; init; }

    public override IMistType Type { get; } = type;
}