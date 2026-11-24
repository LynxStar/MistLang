using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// An inserted copy at an owned position: either a whole-type Clone or a struct construction that
/// copies only its owned objects. The distinction is observable because plain fields may still share.
/// </summary>
internal sealed class CloneExpression(SyntaxNode node, Expression source, bool copiesOwnedFields = false) : Expression(node)
{
    public Expression Source { get; } = source;

    public bool CopiesOwnedFields { get; } = copiesOwnedFields;

    public override IMistType Type { get; } = source.Type;
}