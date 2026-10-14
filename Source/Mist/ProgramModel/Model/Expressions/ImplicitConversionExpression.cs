using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A value reaching the slot it stands in through a declared implicit operator, which is the call a cast
/// makes written invisibly (cast-operators spec, Requirement 7). So <see cref="Operand"/> is that call's
/// argument rather than what the slot holds: a rule reading the slot sees the operator's result, and a rule
/// asking where the operand went finds it passed to <see cref="ParameterType"/>, not moved or copied into the
/// slot. The operand reaches the parameter losslessly, since a second user-declared conversion is refused.
/// </summary>
internal sealed class ImplicitConversionExpression(
    SyntaxNode node,
    Expression operand,
    IMistType targetType,
    IReadOnlyList<ConversionFunction> bestOperators) : Expression(node)
{
    public Expression Operand { get; } = operand;

    public IMistType TargetType { get; } = targetType;

    public CastOperator? Operator { get; } = bestOperators is [var called] ? called.Operator : null;

    public IReadOnlyList<CastOperator> AmbiguousOperators { get; } = bestOperators.Count > 1 ? [.. bestOperators.Select(function => function.Operator)] : [];

    public IMistType? ParameterType { get; } = bestOperators is [var called] ? called.Parameter : null;

    public TypeSubstitution OperatorClosing { get; } = bestOperators is [var called] ? called.Closing : TypeSubstitution.Empty;

    public override IMistType Type { get; } = bestOperators is [var called] ? called.Result : targetType;
}