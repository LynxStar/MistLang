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
}