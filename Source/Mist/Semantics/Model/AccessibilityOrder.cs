namespace Mist.Semantics.Model;

/// <summary>
/// Implements the C# partial order over accessibility levels.
/// The partial order is:
///   Private &lt; Protected &lt; Public
///   Private &lt; Internal  &lt; Public
///   Protected and Internal are incomparable.
/// </summary>
internal static class AccessibilityOrder
{
    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="a"/> is at least as accessible as
    /// <paramref name="b"/> in the C# partial order.
    /// Because Protected and Internal are incomparable, neither is "at least as accessible as"
    /// the other.
    /// </summary>
    public static bool IsAtLeastAsAccessibleAs(Accessibility a, Accessibility b)
    {
        if(a == b)
        {
            return true;
        }

        // Public is at least as accessible as everything.
        if(a == Accessibility.Public)
        {
            return true;
        }

        // Nothing except Public is at least as accessible as Public.
        if(b == Accessibility.Public)
        {
            return false;
        }

        // Everything is at least as accessible as Private.
        if(b == Accessibility.Private)
        {
            return true;
        }

        // Private is not at least as accessible as anything except itself (handled above).
        if(a == Accessibility.Private)
        {
            return false;
        }

        // Remaining case: a is Protected or Internal, b is Protected or Internal, and a != b.
        // Protected and Internal are incomparable.
        return false;
    }
}