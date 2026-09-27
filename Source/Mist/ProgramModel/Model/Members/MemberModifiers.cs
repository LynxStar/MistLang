using Mist.ProgramModel.Model.Enums;

namespace Mist.ProgramModel.Model.Members;

/// <summary>
/// The set of modifiers that can be applied to any declaration.
/// A single structure shared across all declaration kinds (types, methods,
/// properties, fields, indexers, constructors, operators). Validation of
/// legal combinations per context is performed by the consuming builder —
/// this type simply records what was declared.
/// </summary>
public record struct MemberModifiers
{
    /// <summary>The declared accessibility of the member.</summary>
    public Accessibility Accessibility { get; init; }

    /// <summary>Whether the declaration has <c>sealed</c>.</summary>
    public bool IsSealed { get; init; }

    /// <summary>Whether the declaration has <c>static</c>.</summary>
    public bool IsStatic { get; init; }

    /// <summary>Whether the declaration has <c>readonly</c>.</summary>
    public bool IsReadonly { get; init; }

    /// <summary>
    /// Whether the declaration has <c>pure</c>, written or inherited from a pure type. Always set
    /// together with <see cref="IsReadonly"/>, so a rule that asks only about readonly sees a pure
    /// member without being told.
    /// </summary>
    public bool IsPure { get; init; }

    public bool IsLocal { get; init; }

    public bool IsStampable { get; init; }

    /// <summary>Whether the declaration has <c>async</c>.</summary>
    public bool IsAsync { get; init; }

    /// <summary>Whether the declaration has <c>intrinsic</c>.</summary>
    public bool IsIntrinsic { get; init; }

    /// <summary>Whether the declaration has <c>owned</c>.</summary>
    public bool IsOwned { get; init; }

    /// <summary>Whether the declaration has <c>ref</c>.</summary>
    public bool IsRef { get; init; }

    /// <summary>The substructural discipline (affine, linear, relevant, or none).</summary>
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