namespace Mist.ProgramModel.Model;

/// <summary>
/// The ownership tier required on 'this' for a method to be called.
/// Declared explicitly via a <c>with {tier}</c> clause on the method signature.
/// The call site must supply a receiver binding that satisfies this tier.
/// </summary>
public enum ThisTier
{
    /// <summary>No tier requirement — callable from any binding.</summary>
    None = 0,

    /// <summary>Requires at least readonly ref access to 'this'.</summary>
    ReadonlyRef = 1,

    /// <summary>Requires at least frozen access to 'this'.</summary>
    Frozen = 2,

    /// <summary>Requires at least borrowed (exclusive mutable) access to 'this'.</summary>
    Borrowed = 3,

    /// <summary>Consumes 'this' — the caller's binding is moved into the method.</summary>
    Owned = 4,
}