using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Patterns;

/// <summary>
/// <c>p and q and ...</c>, flat rather than nested however the operands were grouped in source.
/// Matching requires all of them, which has two consequences: operands MAY bind and each keeps its own
/// binding, the conjunction itself holding none; and the conjunction narrows to the most specific thing
/// any operand proved, since every one of them proved it.
/// </summary>
internal sealed class ConjunctionPattern(
    SyntaxNode node, IMistType narrowedType, IReadOnlyList<Pattern> operands) : Pattern(node)
{
    public IReadOnlyList<Pattern> Operands { get; } = operands;

    public override IMistType NarrowedType { get; } = narrowedType;

    public override LocalVariable? Binding => null;
}