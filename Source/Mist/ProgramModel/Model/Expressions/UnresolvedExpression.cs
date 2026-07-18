using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// An expression the body binder does not yet resolve. Retains its AST node so a
/// later pass can resolve it; typed as the error sentinel in the meantime.
/// </summary>
internal sealed class UnresolvedExpression(SyntaxNode node) : Expression(node)
{
    public override IMistType Type { get; } = ErrorType.Instance;
}