namespace Mist.Semantics.Model;

/// <summary>
/// The ownership tier required on 'this' for a method to execute safely.
/// Inferred from the method body's usage of owned fields, or declared explicitly with 'with {tier}'.
/// The call site must provide a receiver binding that satisfies this tier.
/// </summary>
public enum ThisTier
{
    /// <summary>No tier requirement — callable from any binding (shared or owned).</summary>
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

/// <summary>
/// Provides ordering comparison and tier mapping helpers for <see cref="ThisTier"/>.
/// </summary>
public static class ThisTierExtensions
{
    /// <summary>
    /// Returns true if the caller's tier satisfies the method's required tier.
    /// A caller satisfies a requirement when their tier is at least as strong (>=) as the required tier.
    /// </summary>
    /// <remarks>
    /// Satisfaction table:
    /// <list type="bullet">
    ///   <item>owned → satisfies all tiers (None, ReadonlyRef, Frozen, Borrowed, Owned)</item>
    ///   <item>borrowed → satisfies None, ReadonlyRef, Frozen, Borrowed</item>
    ///   <item>frozen → satisfies None, ReadonlyRef, Frozen</item>
    ///   <item>readonly ref → satisfies None, ReadonlyRef</item>
    ///   <item>shared (None) → satisfies None only</item>
    /// </list>
    /// </remarks>
    public static bool Satisfies(ThisTier callerTier, ThisTier requiredTier)
    {
        return callerTier >= requiredTier;
    }

    /// <summary>
    /// Maps an <see cref="OwnershipTier"/> (used for bindings) to the corresponding <see cref="ThisTier"/>.
    /// This determines what this-tier requirements a binding at a given ownership tier can satisfy.
    /// </summary>
    public static ThisTier FromOwnershipTier(OwnershipTier ownershipTier)
    {
        return ownershipTier switch
        {
            OwnershipTier.Owned => ThisTier.Owned,
            OwnershipTier.Borrowed => ThisTier.Borrowed,
            OwnershipTier.Frozen => ThisTier.Frozen,
            OwnershipTier.ReadonlyRef => ThisTier.ReadonlyRef,
            OwnershipTier.Unrestricted => ThisTier.None,
            _ => ThisTier.None,
        };
    }
}