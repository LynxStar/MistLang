using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A <c>new T(...)</c> object creation. The constructed type is the expression's type.
/// </summary>
internal sealed class NewExpression(
    SyntaxNode node,
    IMistType type,
    IReadOnlyList<Expression> arguments,
    Constructor? constructor) : Expression(node)
{
    public IReadOnlyList<Expression> Arguments { get; } = arguments;

    /// <summary>
    /// Which constructor the arguments selected, for the reason given on
    /// <see cref="InvocationExpression.Method"/>. Null when no constructor could be named: the type
    /// declares none, the constructed type has no concrete definition to look on, or selection
    /// reported.
    /// </summary>
    public Constructor? Constructor { get; } = constructor;

    public override IMistType Type { get; } = type;
}