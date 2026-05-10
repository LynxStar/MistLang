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

    /// <summary>frozen ref — immutable view that blocks owner mutation, non-storable.</summary>
    FrozenRef,

    /// <summary>ref — borrowed mutable, non-storable.</summary>
    Ref,

    /// <summary>owned — sole ownership, storable, full control.</summary>
    Owned,
}
