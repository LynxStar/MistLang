using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A field of the enclosing type referenced by its bare name, as opposed to through an
/// explicit receiver. <c>this.field</c> binds to a <see cref="MemberAccessExpression"/>
/// over a <see cref="ThisExpression"/> instead; only the implicit form reaches here.
/// </summary>
internal sealed class FieldReferenceExpression(
    SyntaxNode node,
    Field field,
    IMistType? type = null) : Expression(node)
{
    public Field Field { get; } = field;

    public override IMistType Type => type ?? Field.Type;
}