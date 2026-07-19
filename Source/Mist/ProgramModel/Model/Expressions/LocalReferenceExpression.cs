using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A reference to a local variable or parameter. Its type is the local's type.
/// </summary>
internal sealed class LocalReferenceExpression(SyntaxNode node, LocalVariable local) : Expression(node)
{
    /// <summary>
    /// The local variable or parameter being referenced.
    /// </summary>
    public LocalVariable Local { get; } = local;

    public override IMistType Type { get; } = local.Type;
}