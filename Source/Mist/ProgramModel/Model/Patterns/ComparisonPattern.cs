using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Patterns;

/// <summary>
/// A comparison of the scrutinee against a constant: <c>42</c>, <c>"text"</c>, <c>true</c> and
/// <c>&lt; 42</c> are all this one kind. A constant pattern is the <see cref="ComparisonKind.Equal"/>
/// case rather than a kind of its own, because the two differ only in the operator.
/// <para>
/// Narrows to the scrutinee's own type: a value test says nothing about which type holds it, and the
/// type-selection case -- picking a constituent out of a union -- belongs to type-test narrowing.
/// </para>
/// </summary>
internal sealed class ComparisonPattern(
    SyntaxNode node, IMistType scrutineeType, ComparisonKind kind, Expression bound) : Pattern(node)
{
    public ComparisonKind Kind { get; } = kind;

    public Expression Bound { get; } = bound;

    public override IMistType NarrowedType { get; } = scrutineeType;

    public override LocalVariable? Binding => null;
}

internal enum ComparisonKind
{
    Equal,
    LessThan,
    GreaterThan,
    LessThanOrEqual,
    GreaterThanOrEqual,
}