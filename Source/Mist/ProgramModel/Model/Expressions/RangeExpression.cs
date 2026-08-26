using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// <c>a..b</c>, and the three forms that omit a bound. A bound is null where source omitted it, never
/// a substituted zero or length: C# spells an omitted bound by writing nothing, so an absent bound and
/// a bound that happens to be the collection's edge are different code, and only the first is what
/// <c>..2</c> means.
/// </summary>
internal sealed class RangeExpression(
    SyntaxNode node,
    Expression? start,
    Expression? end,
    IMistType type) : Expression(node)
{
    public Expression? Start { get; } = start;

    public Expression? End { get; } = end;

    public override IMistType Type { get; } = type;
}