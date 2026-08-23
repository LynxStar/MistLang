using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Patterns;

/// <summary>
/// A pattern that always matches: <c>var x</c>, <c>var _</c> and a bare <c>_</c>. Testing nothing, it
/// narrows to the scrutinee's own static type rather than to whatever the value turns out to be.
/// </summary>
internal sealed class CatchAllPattern(
    SyntaxNode node, IMistType scrutineeType, LocalVariable? binding) : Pattern(node)
{
    public override IMistType NarrowedType { get; } = scrutineeType;

    public override LocalVariable? Binding { get; } = binding;
}