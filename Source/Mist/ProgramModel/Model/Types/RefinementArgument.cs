using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A dependent type argument that names no value but a refinement of the slot's domain, <c>Cell&lt;Range&gt;</c>:
/// every value of the closing lies inside <see cref="Refinement"/> and inside <see cref="Contract"/>, and
/// which one the object holds is known only when the program runs (runtime-dependent-values Requirements 1.6
/// and 5.3). An <see cref="UnknownValueType"/> to everything that reads the slot only as a value it cannot see,
/// so the backend keeps the value in the object as it does for <c>unknown</c>, and Stage 5 alone reads the
/// refinement.
/// </summary>
internal sealed class RefinementArgument(IMistType domain, IMistType refinement, IMistType? contract)
    : UnknownValueType(TypeDisplay.Mist(refinement), domain)
{
    public IMistType Refinement { get; } = refinement;

    public IMistType? Contract { get; } = contract;
}