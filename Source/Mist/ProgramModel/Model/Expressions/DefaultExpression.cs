using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A <c>default(T)</c> or a bare <c>default</c> that took its type from the position. Carries the type
/// only: what the default OF that type is belongs to whoever consumes this, since a class default is a
/// constructed instance rather than a value the expression can hold.
/// </summary>
internal sealed class DefaultExpression(SyntaxNode node, IMistType type) : Expression(node)
{
    public override IMistType Type { get; } = type;
}