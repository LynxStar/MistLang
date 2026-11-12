using Mist.ProgramModel.Model.Enums;

namespace Mist.ProgramModel.Model.Members;

/// <summary>
/// Shared by every declaration kind. It records what was declared; the consuming builder judges which
/// combinations are legal.
/// </summary>
public record struct MemberModifiers
{
    public Accessibility Accessibility { get; init; }

    public bool IsSealed { get; init; }

    public bool IsStatic { get; init; }

    public bool IsReadonly { get; init; }

    /// <summary>
    /// Whether the declaration has <c>pure</c>, written or inherited from a pure type. Always set
    /// together with <see cref="IsReadonly"/>, so a rule that asks only about readonly sees a pure
    /// member without being told.
    /// </summary>
    public bool IsPure { get; init; }

    public bool IsLocal { get; init; }

    public bool IsStampable { get; init; }

    public bool IsAsync { get; init; }

    public bool IsIntrinsic { get; init; }

    public bool IsOwned { get; init; }

    public bool IsRef { get; init; }

    /// <summary>
    /// Set with <see cref="IsRef"/> where the result is a reference its caller may read but not write
    /// through, which only a CLR <c>ref readonly</c> return gives. Kept apart from <see cref="IsReadonly"/>,
    /// which on a method says its receiver is not mutated: CLR metadata records the two independently.
    /// </summary>
    public bool ReturnsReadonlyRef { get; init; }

    public SubstructuralDiscipline Discipline { get; init; }

    /// <summary>
    /// Set only where a member is projected from CLR metadata, so a result of one can be closed over
    /// <c>| void</c> once its type parameters are known. Members are constructed once here -- by a
    /// builder from Mist source or by a projector from metadata, never cloned between the two -- so
    /// this holds by construction rather than by a discriminator that could be laundered.
    /// </summary>
    public bool IsClrProjected { get; init; }

    /// <summary>
    /// Set where CLR metadata says the result is never null, so the closing over <c>| void</c> that
    /// <see cref="IsClrProjected"/> otherwise implies is not needed. Default false, which keeps a
    /// member whose nullability nothing computed on the pessimistic path it was already on --
    /// silence is oblivious, never a promise.
    /// </summary>
    public bool ClrResultIsNonNull { get; init; }
}