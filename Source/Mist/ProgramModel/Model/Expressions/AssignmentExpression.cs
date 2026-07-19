using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// An assignment expression (<c>target = value</c>).
/// The <see cref="Type"/> is the type of the assigned value.
/// </summary>
internal sealed class AssignmentExpression(
    SyntaxNode node,
    Expression target,
    Expression value,
    IMistType type) : Expression(node)
{
    /// <summary>
    /// The assignment target (variable, field, array element, etc.).
    /// </summary>
    public Expression Target { get; } = target;

    /// <summary>
    /// The value being assigned.
    /// </summary>
    public Expression Value { get; } = value;

    public override IMistType Type { get; } = type;
}