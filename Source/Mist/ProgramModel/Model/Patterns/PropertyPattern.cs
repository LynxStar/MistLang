using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Patterns;

/// <summary>
/// Members resolve against the type established by <see cref="TypeTest"/>, or the present portion of
/// the scrutinee's type for an untyped pattern. The whole-value binding belongs to this node while
/// each member binding belongs to its subpattern, so both survive independently.
/// </summary>
internal sealed class PropertyPattern(
    SyntaxNode node,
    IMistType narrowedType,
    IReadOnlyList<PropertySubpattern> subpatterns,
    LocalVariable? binding = null,
    TypeTestPattern? typeTest = null) : Pattern(node)
{
    public IReadOnlyList<PropertySubpattern> Subpatterns { get; } = subpatterns;

    public TypeTestPattern? TypeTest { get; } = typeTest;

    public override IMistType NarrowedType { get; } = narrowedType;

    public override LocalVariable? Binding { get; } = binding;
}

internal sealed record PropertySubpattern(SyntaxNode Node, string MemberName, Pattern Pattern, IMistType MemberType, LookupResult Member);