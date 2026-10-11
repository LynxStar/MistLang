using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction.Binding;

internal interface IContextualExpressionTrials
{
    bool Fits(UnboundContextualExpression expression, IMistType target);
}