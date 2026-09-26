using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A lambda argument held unbound while its call chooses an overload, because which parameter types it
/// has depends on the choice. A binder intermediate like <see cref="MethodGroupExpression"/>: the call
/// binds it against the chosen parameter once selection is done, and nothing else consumes one.
/// </summary>
internal sealed class UnboundLambdaExpression(LambdaExpressionNode lambda) : Expression(lambda)
{
    public LambdaExpressionNode Lambda { get; } = lambda;

    public override IMistType Type => UnboundLambdaType.Instance;
}