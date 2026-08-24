using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A tuple literal (<c>(42, "text")</c> or <c>(x: 3, y: 4)</c>). Arity is at least two, the grammar
/// requiring a second element rather than repeating the first, so there is no 1-tuple to consider.
/// <para>
/// <see cref="Type"/> is the INTERNED structural tuple, never a
/// <see cref="Types.NamedTupleType"/>: names written at a literal are use-site metadata kept in
/// <see cref="ElementNames"/>, and wrapping them into the type would make one interned tuple per
/// spelling and put two literals of the same shape at different identities. A declaration site is
/// the asymmetric case -- there the names ARE part of what was declared, so
/// <see cref="Construction.Resolution.ScopeResolver"/> does wrap.
/// </para>
/// </summary>
internal sealed class TupleLiteralExpression(
    SyntaxNode node,
    IReadOnlyList<Expression> elements,
    IReadOnlyList<string?> elementNames,
    IMistType type) : Expression(node)
{
    /// <summary>
    /// The literal's elements in positional order.
    /// </summary>
    public IReadOnlyList<Expression> Elements { get; } = elements;

    /// <summary>
    /// Element names in positional order, aligned by index with <see cref="Elements"/>. Null entries
    /// indicate an element written without a name.
    /// </summary>
    public IReadOnlyList<string?> ElementNames { get; } = elementNames;

    public override IMistType Type { get; } = type;
}