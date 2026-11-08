using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// <c>target op= value</c>, or an increment (<c>x++</c>, <c>--x</c>), whose value is the literal 1. It stands for
/// <c>target = target op value</c> with the target evaluated once: a read of the target followed by a write of
/// <see cref="ResultType"/>, which Stage 5 refuses where the long form's value would be refused. <see cref="Type"/>
/// is the storage type, what the expression yields. <see cref="GuardsRemainder"/> marks a remainder whose divisor
/// is proven nonzero but may be -1 over a dividend that may be the minimum, where the long form calls the guarded
/// helper rather than the CLR operator, which would throw.
/// </summary>
internal sealed class CompoundAssignmentExpression(
    SyntaxNode node,
    Expression target,
    BinaryOperatorKind operatorKind,
    Expression value,
    IMistType type,
    Operator? @operator,
    IncrementForm incrementForm) : Expression(node)
{
    public Expression Target { get; } = target;

    public BinaryOperatorKind OperatorKind { get; } = operatorKind;

    public Expression Value { get; } = value;

    public Operator? Operator { get; } = @operator;

    public IncrementForm IncrementForm { get; } = incrementForm;

    public IMistType ResultType { get; init; } = type;

    public TypeSubstitution OperatorClosing { get; init; } = TypeSubstitution.Empty;

    public IReadOnlyList<IMistType>? ParameterTypes { get; init; }

    public IReadOnlyList<ClosedOperator> AmbiguousOperators { get; init; } = [];

    public bool GuardsRemainder { get; init; }

    public override IMistType Type { get; } = type;
}

internal enum IncrementForm
{
    None,
    Prefix,
    Postfix,
}