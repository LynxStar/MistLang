using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Patterns;

/// <summary>
/// <c>p or q or ...</c>, flat rather than nested however the operands were grouped in source. No
/// operand may bind, since matching establishes only that one of them did.
/// <para>
/// Narrows to the scrutinee's own type and NOT to the union of its operands, even though that union is
/// what a match proves. Interning one would fabricate a union the program never names, and the emitter
/// discovers carrier structs by reading the interner -- so the honest type would add a struct to
/// <c>__Unions.g.cs</c> as a side effect of writing a pattern. With no binding to type, nothing is
/// lost by declining.
/// </para>
/// </summary>
internal sealed class DisjunctionPattern(
    SyntaxNode node, IMistType scrutineeType, IReadOnlyList<Pattern> operands) : Pattern(node)
{
    public IReadOnlyList<Pattern> Operands { get; } = operands;

    public override IMistType NarrowedType { get; } = scrutineeType;

    public override LocalVariable? Binding => null;
}