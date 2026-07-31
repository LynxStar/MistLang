using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// The storage behind a property, named by <c>field</c> from inside one of its accessors.
/// <para>
/// The property is the slot's whole identity: there is exactly one slot per property, its type is
/// the property's, and Mist has no attribute target that could need somewhere else to attach. So a
/// separate slot entity would carry nothing the property does not.
/// </para>
/// <para>
/// A reference rather than a lowering, so each backend decides what the slot IS. C# can name its
/// own synthesized field and never declare one; a backend without auto-properties has to
/// synthesize real storage from the property's name and type.
/// </para>
/// </summary>
internal sealed class BackingFieldReferenceExpression(SyntaxNode node, Property property) : Expression(node)
{
    public Property Property { get; } = property;

    public override IMistType Type => Property.Type;
}