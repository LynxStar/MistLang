using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// How a <c>void</c> token in expression position was read. The token resolves three ways
/// from the target type alone: the unit VALUE when a void is expected, ABSENCE when an
/// optional is, and otherwise the type literal -- which is not this expression at all but a
/// <see cref="TypeLiteralExpression"/> over Void.
/// </summary>
internal enum VoidReading
{
    Singleton,
    Absence,
}

internal sealed class VoidLiteralExpression(
    SyntaxNode node,
    VoidReading reading,
    IMistType type) : Expression(node)
{
    public VoidReading Reading { get; } = reading;

    public override IMistType Type { get; } = type;
}