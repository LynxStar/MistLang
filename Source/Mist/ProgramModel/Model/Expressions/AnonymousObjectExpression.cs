using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A <c>new { X = 1, y }</c> literal. The member NAMES live on <see cref="AnonymousType"/> rather
/// than here, because unlike a tuple literal's element names they are part of the type's identity --
/// see the interning note there -- so the literal carries only the values, aligned by index.
/// </summary>
internal sealed class AnonymousObjectExpression(
    SyntaxNode node, IReadOnlyList<Expression> values, AnonymousType type) : Expression(node)
{
    public IReadOnlyList<Expression> Values { get; } = values;

    public AnonymousType AnonymousType { get; } = type;

    public override IMistType Type { get; } = type;
}