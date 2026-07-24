namespace Mist.ProgramModel.Model.Enums;

/// <summary>
/// The ownership classification of a storage location (field, parameter, local).
/// </summary>
public enum OwnershipKind
{
    /// <summary>No ownership annotation. Standard reference semantics.</summary>
    Unrestricted,

    /// <summary>Owned — the holder is responsible for the value's lifetime.</summary>
    Owned,

    /// <summary>Borrowed — a temporary reference that does not own the value.</summary>
    Borrowed,

    /// <summary>
    /// Frozen — an immutable view that also blocks the owner from mutating while outstanding.
    /// Valid on parameters and bindings; not permitted on field declarations (rejected during
    /// validation). Kept in the shared enum so ownership requirements remain directly comparable.
    /// </summary>
    Frozen,
}