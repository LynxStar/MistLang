using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Patterns;

/// <summary>
/// A test that the scrutinee holds a named type, optionally binding it: <c>int i</c>, <c>int _</c>
/// and a bare <c>int</c> are all this one kind, differing only in whether a binding survives.
/// </summary>
internal sealed class TypeTestPattern(
    SyntaxNode node, IMistType testedType, LocalVariable? binding) : Pattern(node)
{
    public override IMistType NarrowedType { get; } = testedType;

    public override LocalVariable? Binding { get; } = binding;
}