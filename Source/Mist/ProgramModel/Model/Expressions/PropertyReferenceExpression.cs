using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A property of the enclosing type referenced by its bare name, as opposed to through an
/// explicit receiver. <c>this.Prop</c> binds to a <see cref="MemberAccessExpression"/> over a
/// <see cref="ThisExpression"/> instead; only the implicit form reaches here.
/// <para>
/// Carries no read-or-write distinction, matching <see cref="FieldReferenceExpression"/>: an
/// assignment holds this as its target, so the surrounding expression is what says which it is.
/// </para>
/// </summary>
internal sealed class PropertyReferenceExpression(SyntaxNode node, Property property) : Expression(node)
{
    public Property Property { get; } = property;

    public override IMistType Type => Property.Type;
}