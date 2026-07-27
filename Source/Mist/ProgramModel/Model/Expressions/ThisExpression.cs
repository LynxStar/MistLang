using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// The <c>this</c> reference inside an instance member. Its type is the enclosing type.
/// </summary>
internal sealed class ThisExpression(
    SyntaxNode node,
    IMistType type) : Expression(node)
{
    public override IMistType Type { get; } = type;
}