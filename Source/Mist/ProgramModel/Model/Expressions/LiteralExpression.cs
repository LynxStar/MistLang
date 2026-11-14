using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

internal sealed class LiteralExpression(SyntaxNode node, object? value, IMistType type) : Expression(node)
{
    /// <summary>
    /// The literal's constant value (e.g. a boxed <see cref="long"/> for an integer
    /// literal). Null represents an absent value.
    /// </summary>
    public object? Value { get; } = value;

    public override IMistType Type { get; } = type;
}