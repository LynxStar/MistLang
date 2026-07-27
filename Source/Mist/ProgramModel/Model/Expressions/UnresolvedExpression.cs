using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// An expression the body binder does not yet handle. Retains its AST node so a later
/// pass can resolve it; typed as the error sentinel in the meantime. This means "not
/// implemented", not "invalid": no diagnostic is reported for it, and it must not be used
/// for code that is genuinely in error. Use <see cref="ErrorExpression"/> for that.
/// </summary>
internal sealed class UnresolvedExpression(SyntaxNode node) : Expression(node)
{
    public override IMistType Type { get; } = ErrorType.Instance;
}