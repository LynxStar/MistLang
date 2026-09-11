using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Patterns;

/// <summary>
/// <c>{ Member: p, ... }</c>. Each subpattern names a member of the scrutinee's own type and matches
/// against what that member holds, so the member's type is the subpattern's scrutinee and nesting
/// narrows progressively.
/// <para>
/// Keeps no binding of its own, and not because a property pattern could not carry one -- the grammar
/// spells a trailing name -- but because nothing can reach that slot yet, so a binder here would be a
/// slot no source fills. The binders a property pattern contributes therefore all live on its
/// SUBPATTERNS, which is where <see cref="Pattern.Binding"/> being singular per node puts them: several
/// out of one pattern comes from the recursion.
/// </para>
/// </summary>
internal sealed class PropertyPattern(
    SyntaxNode node, IMistType narrowedType, IReadOnlyList<PropertySubpattern> subpatterns) : Pattern(node)
{
    public IReadOnlyList<PropertySubpattern> Subpatterns { get; } = subpatterns;

    public override IMistType NarrowedType { get; } = narrowedType;

    public override LocalVariable? Binding => null;
}

internal sealed record PropertySubpattern(string MemberName, Pattern Pattern, IMistType MemberType);