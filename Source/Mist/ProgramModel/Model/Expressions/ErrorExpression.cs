using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// An expression that is invalid and has already been diagnosed. Deliberately distinct
/// from <see cref="UnresolvedExpression"/>: that one means the binder does not handle
/// the construct yet and a later pass is expected to resolve it, whereas this one is
/// final. No later pass will resolve an <see cref="ErrorExpression"/>, and a diagnostic
/// describing why has already been reported, so consumers must not report again.
/// </summary>
internal sealed class ErrorExpression(SyntaxNode node) : Expression(node)
{
    public override IMistType Type { get; } = ErrorType.Instance;
}