using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// The element a statement reads for itself -- a foreach's current item, a deconstruction's tuple element --
/// standing as the operand of the conversion that element takes into its variable. Neither statement has an
/// expression for the element, and one built from the collection or the tuple would be evaluated, walked and
/// emitted once per read.
/// </summary>
internal sealed class ElementReadExpression(SyntaxNode node, IMistType type, Expression? ownershipSource = null) : Expression(node)
{
    // Provenance names an already evaluated owner, so the body walker does not evaluate this edge again.
    public Expression? OwnershipSource { get; } = ownershipSource;

    public override IMistType Type { get; } = type;
}