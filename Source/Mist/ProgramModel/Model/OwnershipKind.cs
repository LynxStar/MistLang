namespace Mist.ProgramModel.Model;

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
}