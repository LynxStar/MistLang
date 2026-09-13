using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// What a call's arguments picked out of its candidates. A null <see cref="Selected"/> leaves exactly
/// one of the five failure members non-empty, since each carries a different diagnostic.
/// </summary>
internal sealed class OverloadChoice<T>
    where T : Method
{
    public required T? Selected { get; init; }

    public bool NoArityMatch { get; init; }

    /// <summary>
    /// The receiver's type arguments and the method's own, together. <see cref="Selected"/> is the
    /// declaration, so its signature has to be read through this.
    /// </summary>
    public TypeSubstitution Substitution { get; init; } = TypeSubstitution.Empty;

    /// <summary>
    /// The candidates that tied, empty unless the call is ambiguous. Each is applicable and none is
    /// beaten by another, so choosing between them would be arbitrary.
    /// </summary>
    public IReadOnlyList<T> Ambiguous { get; init; } = [];

    /// <summary>
    /// The arity matches, empty unless every one of them failed applicability. Distinct from
    /// <see cref="Ambiguous"/> in what it rests on: a tie is several positive results, this is none.
    /// </summary>
    public IReadOnlyList<T> Unresolvable { get; init; } = [];

    /// <summary>
    /// The arity matches, empty unless every one of them is generic and left a type parameter
    /// undetermined.
    /// </summary>
    public IReadOnlyList<T> Uninferred { get; init; } = [];

    public TypeArgumentConflict? UninferredConflict { get; init; }

    /// <summary>
    /// The arity matches, empty unless the call stated type arguments and no candidate declares that
    /// many type parameters.
    /// </summary>
    public IReadOnlyList<T> TypeArgumentMismatch { get; init; } = [];
}