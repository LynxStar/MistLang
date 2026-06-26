namespace Mist.Semantics.Model;

/// <summary>
/// The ref kind of a parameter, return type, or local variable.
/// Orthogonal to OwnershipTier — a symbol has exactly one of each.
/// </summary>
public enum RefKind
{
    /// <summary>No ref annotation. Standard pass-by-value / return-by-value.</summary>
    None,

    /// <summary>ref — mutable reference to a storage location.</summary>
    Ref,

    /// <summary>ref readonly — readonly reference to a storage location.</summary>
    RefReadonly,
}