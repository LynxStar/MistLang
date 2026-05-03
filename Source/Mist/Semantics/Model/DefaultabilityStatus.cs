namespace Mist.Semantics.Model;

/// <summary>
/// Describes whether a class or struct type can produce a compile-time default value.
/// </summary>
public enum DefaultabilityStatus
{
    /// <summary>Not yet computed (initial state).</summary>
    Unknown,

    /// <summary>All fields are defaultable; no explicit block needed.</summary>
    Inferred,

    /// <summary>Has an explicit default { } block.</summary>
    Explicit,

    /// <summary>Has non-defaultable fields without type bindings.</summary>
    NotDefaultable,
}
