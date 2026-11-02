using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

internal sealed class ResultPropagationExpression(
    SyntaxNode node, Expression operand, ResultTypes result, UnionType inspection, IMistType successCase, IMistType failureCase, ElementReadExpression errorValue, ReturnStatement failureReturn) : Expression(node)
{
    public Expression Operand { get; } = operand;

    public ResultTypes Result { get; } = result;

    public UnionType Inspection { get; } = inspection;

    public IMistType SuccessCase { get; } = successCase;

    public IMistType FailureCase { get; } = failureCase;

    public ElementReadExpression ErrorValue { get; } = errorValue;

    public ReturnStatement FailureReturn { get; } = failureReturn;

    public override IMistType Type => Result.Success;
}