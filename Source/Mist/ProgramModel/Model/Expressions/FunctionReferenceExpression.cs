using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A method named as a VALUE rather than called: the result of converting a method group against a
/// function type. Distinct from <see cref="MethodReferenceExpression"/>, whose type is the method's
/// RESULT because it exists to be the receiver of an invocation -- here the whole function is the value,
/// so the type is the function type it converted to.
/// </summary>
internal sealed class FunctionReferenceExpression(
    SyntaxNode node,
    Expression? receiver,
    Method method,
    IMistType type) : Expression(node)
{
    /// <summary>
    /// What the method was reached through, or null for a bare name.
    /// </summary>
    public Expression? Receiver { get; } = receiver;

    public Method Method { get; } = method;

    public IReadOnlyList<IMistType> TypeArguments { get; init; } = [];

    public IReadOnlyList<SlotCapture> Captures { get; init; } = [];

    /// <summary>
    /// The method's parameter and return types under its closing, which differ from <see cref="Type"/>'s
    /// where a slot pair waits on Stage 5 to prove the two one type.
    /// </summary>
    public IReadOnlyList<IMistType> ClosedParameterTypes { get; init; } = [];

    public IMistType? ClosedReturnType { get; init; }

    public IReadOnlyList<IMistType> ContextTypes { get; init; } = ContextBinding.TypesOf(method);

    public override IMistType Type { get; } = type;
}