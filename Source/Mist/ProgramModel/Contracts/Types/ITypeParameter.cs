namespace Mist.ProgramModel.Contracts.Types;

public interface ITypeParameter : IMistType
{
    /// <summary>
    /// Zero-based, within its declaration's type parameter list.
    /// </summary>
    int Ordinal { get; }

    /// <summary>
    /// The type whose values fill this slot: <c>Standard.Type</c> for a plain <c>T</c>, the annotated
    /// value type for a dependent <c>N:int</c>. Null until resolved in Phase 2.
    /// </summary>
    IMistType? Domain { get; }

    /// <summary>
    /// From where clauses; empty until resolved in Phase 2.
    /// </summary>
    IReadOnlyList<ITypeParameterConstraint> Constraints { get; }
}