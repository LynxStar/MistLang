namespace Mist.ProgramModel.Contracts;

/// <summary>
/// The set of modifiers that can be applied to a member declaration.
/// A single structure shared across all member kinds (methods, properties,
/// fields, indexers, constructors, operators). Validation of legal combinations
/// is performed elsewhere — this type simply records what was declared.
/// </summary>
public record struct MemberModifiers
{
    /// <summary>Whether the member is declared <c>public</c>.</summary>
    public bool IsPublic { get; init; }

    /// <summary>Whether the member is declared <c>private</c>.</summary>
    public bool IsPrivate { get; init; }

    /// <summary>Whether the member is declared <c>static</c>.</summary>
    public bool IsStatic { get; init; }

    /// <summary>Whether the member is declared <c>readonly</c>.</summary>
    public bool IsReadonly { get; init; }

    /// <summary>Whether the member is declared <c>async</c>.</summary>
    public bool IsAsync { get; init; }

    /// <summary>Whether the member is declared <c>intrinsic</c>.</summary>
    public bool IsIntrinsic { get; init; }
}