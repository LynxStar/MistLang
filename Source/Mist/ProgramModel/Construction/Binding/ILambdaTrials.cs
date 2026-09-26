using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// What selection may ask of an <see cref="UnboundLambdaExpression"/> while it chooses. Binding a lambda
/// is the expression binder's, so each answer is read off a binding the caller discards; the binding
/// that stays is made once, against the parameter the chosen overload settles on.
/// </summary>
internal interface ILambdaTrials
{
    IReadOnlyList<IMistType?> StatedParameterTypes(UnboundLambdaExpression lambda);

    IMistType? ResultType(UnboundLambdaExpression lambda, IReadOnlyList<IMistType> parameterTypes);

    bool Fits(UnboundLambdaExpression lambda, IMistType parameterType);

    IMistType ComparedType(UnboundLambdaExpression lambda, IMistType left, IMistType right);
}