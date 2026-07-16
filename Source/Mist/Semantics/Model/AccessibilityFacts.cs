using Mist.Generated;

namespace Mist.Semantics.Model;

/// <summary>
/// The single source of truth for reducing a modifier list into an explicit
/// <see cref="Accessibility"/> value and a sealed flag (classic pipeline).
/// This helper performs no diagnostic reporting — callers are responsible for
/// emitting diagnostics when the access modifier count exceeds one.
/// </summary>
internal static class AccessibilityFacts
{
    /// <summary>
    /// Returns the explicit <see cref="Accessibility"/> if exactly one access modifier is present;
    /// <see langword="null"/> if none is present (caller resolves the scope-based default).
    /// The <paramref name="accessModifierCount"/> output indicates how many access modifiers were
    /// found in the list. When the count is greater than one, the caller should report
    /// a multiple-access-modifier diagnostic.
    /// The <c>sealed</c> modifier is orthogonal and never counts toward <paramref name="accessModifierCount"/>.
    /// </summary>
    public static Accessibility? TryReduceExplicit(
        IReadOnlyList<ModifierNode> modifiers, out int accessModifierCount)
    {
        accessModifierCount = 0;
        Accessibility resolved = default;

        foreach(var mod in modifiers)
        {
            switch(mod)
            {
                case PublicModifierModifierNode:
                    resolved = Accessibility.Public;
                    accessModifierCount++;
                    break;
                case PrivateModifierModifierNode:
                    resolved = Accessibility.Private;
                    accessModifierCount++;
                    break;
                case ProtectedModifierModifierNode:
                    resolved = Accessibility.Protected;
                    accessModifierCount++;
                    break;
                case InternalModifierModifierNode:
                    resolved = Accessibility.Internal;
                    accessModifierCount++;
                    break;
            }
        }

        if(accessModifierCount == 0)
        {
            return null;
        }

        // When more than one access modifier is present, we still return the last one
        // encountered so the caller has something to work with, but the count signals
        // the invalid combination.
        return resolved;
    }

    /// <summary>
    /// Returns <see langword="true"/> if the <c>sealed</c> modifier is present in the list,
    /// independent of any access modifier.
    /// </summary>
    public static bool HasSealed(IReadOnlyList<ModifierNode> modifiers)
    {
        foreach(var mod in modifiers)
        {
            if(mod is SealedModifierModifierNode)
            {
                return true;
            }
        }

        return false;
    }
}