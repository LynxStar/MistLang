using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// Ordinary assignments carry the storage result type; coalescing statements carry the present value type.
/// </summary>
internal sealed class AssignmentExpression(
    SyntaxNode node,
    Expression target,
    Expression value,
    IMistType type,
    bool isCoalescing = false) : Expression(node)
{
    public Expression Target { get; } = target;

    public bool IsCoalescing { get; } = isCoalescing;

    public Expression Value { get; } = value;

    public override IMistType Type { get; } = type;
}