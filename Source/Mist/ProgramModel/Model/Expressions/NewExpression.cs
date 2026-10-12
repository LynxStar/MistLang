using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
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
    IReadOnlyList<ObjectInitializerAssignment> initializer,
    IReadOnlyList<IMistType>? parameterTypes) : Expression(node)
{
    public IReadOnlyList<Expression> Arguments { get; } = arguments;

    /// <summary>
    /// Which constructor the arguments selected, for the reason given on
    /// <see cref="InvocationExpression.Method"/>. Null when no constructor could be named: the type
    /// declares none, the constructed type has no concrete definition to look on, or selection
    /// reported.
    /// </summary>
    public Constructor? Constructor { get; } = constructor;

    /// <summary>
    /// The type each argument position was bound against, as <see cref="InvocationExpression.ParameterTypes"/>
    /// records it for a call. Null when no constructor was selected.
    /// </summary>
    public IReadOnlyList<IMistType>? ParameterTypes { get; } = parameterTypes;

    public IReadOnlyList<IMistType> ContextTypes { get; init; } = ContextBinding.TypesOf(constructor);

    public IReadOnlyList<ObjectInitializerAssignment> Initializer { get; } = initializer;

    public override IMistType Type { get; } = type;
}

/// <summary>
/// One <c>Name = value</c> of an initializer block. Not an <see cref="AssignmentExpression"/> over a
/// member reference: the member belongs to the object being constructed, which nothing at this
/// position denotes, so there is no receiver to hold and nothing for a later pass to mistake for an
/// assignment to a name in scope.
/// </summary>
internal sealed class ObjectInitializerAssignment(
    SyntaxNode node, string? memberName, Expression value, IMistType? targetType, Method? add = null, LookupResult? member = null)
{
    public SyntaxNode Node { get; } = node;

    /// <summary>
    /// The member assigned, or null for a COLLECTION element -- <c>{ 1, 2 }</c> names nothing and
    /// stands for a call instead, which <see cref="Add"/> holds.
    /// </summary>
    public string? MemberName { get; } = memberName;

    public LookupResult? Member { get; } = member;

    public bool IsTargetFailed { get; init; }

    /// <summary>
    /// The <c>Add</c> overload a collection element resolved to. Recorded because emission writes
    /// the element back as C#'s own initializer syntax and lets Roslyn resolve Add AGAIN -- so
    /// without this nothing in the model would ever have chosen a member, and a program whose
    /// element matches no Add would reach the generated file to fail there.
    /// </summary>
    public Method? Add { get; } = add;

    public IReadOnlyList<IMistType>? AddParameterTypes { get; init; }

    public Expression Value { get; } = value;

    public IMistType? TargetType { get; } = targetType;
}