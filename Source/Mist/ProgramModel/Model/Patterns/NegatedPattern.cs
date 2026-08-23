using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Patterns;

/// <summary>
/// <c>not p</c>. Narrows to the scrutinee's own type and keeps no binding, in both cases because
/// failing a test establishes nothing positive -- there is no type the scrutinee is now known to hold,
/// and no value to have bound.
/// </summary>
internal sealed class NegatedPattern(
    SyntaxNode node, IMistType scrutineeType, Pattern operand) : Pattern(node)
{
    public Pattern Operand { get; } = operand;

    public override IMistType NarrowedType { get; } = scrutineeType;

    public override LocalVariable? Binding => null;
}