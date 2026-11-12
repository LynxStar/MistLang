namespace Mist.ProgramModel.Model.Enums;

public enum OwnershipKind
{
    /// <summary>No ownership annotation. Standard reference semantics.</summary>
    Unrestricted,

    Owned,

    Borrowed,

    /// <summary>
    /// An immutable view that also blocks the owner from mutating while outstanding. Rejected on a field
    /// declaration, but kept in the shared enum so ownership requirements remain directly comparable.
    /// </summary>
    Frozen,
}