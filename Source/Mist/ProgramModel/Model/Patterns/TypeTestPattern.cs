using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Patterns;

/// <summary>
/// A test that the scrutinee holds a named type, optionally binding it: <c>int i</c>, <c>int _</c>
/// and a bare <c>int</c> are all this one kind, differing only in whether a binding survives.
/// </summary>
internal sealed class TypeTestPattern(
    SyntaxNode node,
    IMistType testedType,
    LocalVariable? binding,
    IMistType? constituent = null,
    Pattern? refinement = null) : Pattern(node)
{
    /// <summary>
    /// The predicate a REFINED tested type narrows by, bound against its base, or null where the
    /// tested type carries no refinement. Held here rather than looked up at emission because a
    /// refinement's predicate is an AST node until something binds it, and only the binder can.
    /// </summary>
    public Pattern? Refinement { get; } = refinement;

    /// <summary>
    /// Which constituent of a union-typed scrutinee the test goes through, or null where the scrutinee is
    /// not a union with a carrier. Resolved here rather than left to emission because the SAME lookup
    /// decides whether the test has any lowering at all: a type belonging to no constituent names no
    /// accessor, and that is reported.
    /// </summary>
    public IMistType? Constituent { get; } = constituent;

    public override IMistType NarrowedType { get; } = testedType;

    public override LocalVariable? Binding { get; } = binding;
}