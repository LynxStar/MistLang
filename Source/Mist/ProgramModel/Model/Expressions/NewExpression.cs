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
    Constructor? constructor,
    IReadOnlyList<ObjectInitializerAssignment> initializer) : Expression(node)
{
    public IReadOnlyList<Expression> Arguments { get; } = arguments;

    /// <summary>
    /// Which constructor the arguments selected, for the reason given on
    /// <see cref="InvocationExpression.Method"/>. Null when no constructor could be named: the type
    /// declares none, the constructed type has no concrete definition to look on, or selection
    /// reported.
    /// </summary>
    public Constructor? Constructor { get; } = constructor;

    public IReadOnlyList<ObjectInitializerAssignment> Initializer { get; } = initializer;

    public override IMistType Type { get; } = type;
}

/// <summary>
/// One <c>Name = value</c> of an initializer block. Not an <see cref="AssignmentExpression"/> over a
/// member reference: the member belongs to the object being constructed, which nothing at this
/// position denotes, so there is no receiver to hold and nothing for a later pass to mistake for an
/// assignment to a name in scope.
/// </summary>
internal sealed class ObjectInitializerAssignment(SyntaxNode node, string memberName, Expression value)
{
    public SyntaxNode Node { get; } = node;

    public string MemberName { get; } = memberName;

    public Expression Value { get; } = value;
}