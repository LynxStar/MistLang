namespace Mist.Semantics.Model;

/// <summary>
/// The ownership tier of a variable, parameter, or return type.
/// </summary>
public enum OwnershipTier
{
    /// <summary>No ownership annotation. Standard reference semantics.</summary>
    Unrestricted,

    /// <summary>readonly ref — live readonly view, non-storable.</summary>
    ReadonlyRef,

    /// <summary>frozen — immutable view that blocks owner mutation, non-storable.</summary>
    Frozen,

    /// <summary>borrowed — borrowed mutable, non-storable.</summary>
    Borrowed,

    /// <summary>owned — sole ownership, storable, full control.</summary>
    Owned,
}