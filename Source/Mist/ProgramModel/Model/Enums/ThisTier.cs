namespace Mist.ProgramModel.Model.Enums;

/// <summary>
/// Declared by a <c>with {tier}</c> clause on a method signature; a call site's receiver binding must
/// satisfy it.
/// </summary>
public enum ThisTier
{
    None = 0,

    ReadonlyRef = 1,

    Frozen = 2,

    /// <summary>Requires at least borrowed (exclusive mutable) access to 'this'.</summary>
    Borrowed = 3,

    /// <summary>Consumes 'this' — the caller's binding is moved into the method.</summary>
    Owned = 4,
}