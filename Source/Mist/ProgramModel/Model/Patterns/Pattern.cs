using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Patterns;

/// <summary>
/// A pattern within a resolved body. Concrete kinds are matched by type, as expressions and statements
/// are.
/// <para>
/// <see cref="NarrowedType"/> and <see cref="Binding"/> ARE the narrowing: what matching proves about
/// the scrutinee, and the name that proof is available under. Intrinsic to the pattern rather than
/// held in a table keyed by the branch it guards, which is what lets a binding be read in the same
/// condition that introduced it. <see cref="Binding"/> is singular because a pattern with several
/// binders has them on its SUBPATTERNS, so multiplicity comes from the recursion and no list belongs
/// here.
/// </para>
/// </summary>
internal abstract class Pattern(SyntaxNode node)
{
    public SyntaxNode Node { get; } = node;

    public abstract IMistType NarrowedType { get; }

    public abstract LocalVariable? Binding { get; }
}