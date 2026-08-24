using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Patterns;

/// <summary>
/// <c>(p, q, ...)</c>. Each element pattern matches against the element the scrutinee holds at that
/// position, so an element's type is its subpattern's scrutinee and nesting narrows progressively.
/// <para>
/// The one pattern kind that carries a binding of its own ALONGSIDE its subpatterns'.
/// <c>x is (int a, int b) whole</c> keeps three names, and only one of them belongs to this node --
/// which is what <see cref="Pattern.Binding"/> being singular per node means, rather than a pattern
/// having at most one binder anywhere beneath it.
/// </para>
/// </summary>
internal sealed class TuplePattern(
    SyntaxNode node,
    IReadOnlyList<Pattern> elements,
    IMistType narrowedType,
    IMistType? testedType,
    LocalVariable? binding) : Pattern(node)
{
    /// <summary>
    /// The element patterns in positional order.
    /// </summary>
    public IReadOnlyList<Pattern> Elements { get; } = elements;

    /// <summary>
    /// The tuple type matching must establish at RUNTIME, or null where the scrutinee already is a
    /// tuple and the pattern only reads its elements out. Resolved here rather than at emission because
    /// the two answers are different C# constructs and not two spellings of one: a scrutinee already
    /// typed as a tuple takes a bare positional pattern, while one that is not has to name the
    /// ValueTuple first, C# reading a bare positional pattern as a deconstruction of whatever it was
    /// given.
    /// </summary>
    public IMistType? TestedType { get; } = testedType;

    public override IMistType NarrowedType { get; } = narrowedType;

    public override LocalVariable? Binding { get; } = binding;
}