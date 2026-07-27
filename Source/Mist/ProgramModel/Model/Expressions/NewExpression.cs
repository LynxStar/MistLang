using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A <c>new T(...)</c> object creation. The constructed type is the expression's type.
/// <para>
/// Which constructor overload the arguments select is not resolved here. The C# backend
/// re-emits the call and lets Roslyn choose, so only the constructed type matters for
/// emission. A backend that had to pick would need overload resolution added.
/// </para>
/// </summary>
internal sealed class NewExpression(
    SyntaxNode node,
    IMistType type,
    IReadOnlyList<Expression> arguments) : Expression(node)
{
    public IReadOnlyList<Expression> Arguments { get; } = arguments;

    public override IMistType Type { get; } = type;
}