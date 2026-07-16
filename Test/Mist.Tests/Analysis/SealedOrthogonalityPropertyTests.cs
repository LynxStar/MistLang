// Feature: full-accessibility-model, Property 2: Sealed is orthogonal to accessibility
//
// **Validates: Requirements 2.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Generated;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying that sealed is orthogonal to accessibility.
///
/// Property 2: For any modifier list, reducing the list with the sealed modifier
/// added yields the same Accessibility value as reducing it without sealed, and
/// sets the Sealed_Flag; reducing a list without sealed leaves the Sealed_Flag unset.
///
/// **Validates: Requirements 2.5**
/// </summary>
public class SealedOrthogonalityPropertyTests
{
    // ─── Non-access modifier pool (excludes access modifiers and sealed) ─

    private static readonly ModifierNode[] NonAccessModifiers =
    [
        new StaticModifierModifierNode(),
        new AsyncModifierModifierNode(),
        new IntrinsicModifierModifierNode(),
        new ReadonlyModifierModifierNode(),
    ];

    // ─── Access modifier pool ────────────────────────────────────────────

    private static readonly ModifierNode[] AccessModifiers =
    [
        new PublicModifierModifierNode(),
        new PrivateModifierModifierNode(),
        new ProtectedModifierModifierNode(),
        new InternalModifierModifierNode(),
    ];

    // ─── Scenario type ───────────────────────────────────────────────────

#pragma warning disable CA1034
    /// <summary>
    /// Represents a modifier list scenario: a base modifier list (with 0 or 1 access
    /// modifier plus zero or more non-access modifiers) that does NOT contain sealed.
    /// The test will then create with-sealed and without-sealed variants.
    /// </summary>
    public sealed class ModifierListScenario
    {
        public IReadOnlyList<ModifierNode> BaseModifiers { get; }
        public string Description { get; }

        public ModifierListScenario(IReadOnlyList<ModifierNode> baseModifiers, string description)
        {
            BaseModifiers = baseModifiers;
            Description = description;
        }

        public override string ToString()
        {
            return Description;
        }
    }
#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<ModifierListScenario> GenModifierListScenario()
    {
        // Generate: optionally one access modifier + a subset of non-access modifiers
        return from includeAccess in Gen.Choose(0, 1)
               from accessIndex in Gen.Choose(0, AccessModifiers.Length - 1)
               from nonAccessMask in Gen.Choose(0, (1 << NonAccessModifiers.Length) - 1)
               select BuildScenario(includeAccess == 1, accessIndex, nonAccessMask);
    }

    public static Arbitrary<ModifierListScenario> ArbitraryModifierListScenario()
    {
        return GenModifierListScenario().ToArbitrary();
    }

    private static ModifierListScenario BuildScenario(
        bool includeAccess, int accessIndex, int nonAccessMask)
    {
        var modifiers = ImmutableArray.CreateBuilder<ModifierNode>();
        var parts = new List<string>();

        if(includeAccess)
        {
            modifiers.Add(AccessModifiers[accessIndex]);
            parts.Add(AccessModifiers[accessIndex] switch
            {
                PublicModifierModifierNode => "public",
                PrivateModifierModifierNode => "private",
                ProtectedModifierModifierNode => "protected",
                InternalModifierModifierNode => "internal",
                _ => "unknown",
            });
        }

        for(var i = 0; i < NonAccessModifiers.Length; i++)
        {
            if((nonAccessMask & (1 << i)) != 0)
            {
                modifiers.Add(NonAccessModifiers[i]);
                parts.Add(NonAccessModifiers[i] switch
                {
                    StaticModifierModifierNode => "static",
                    AsyncModifierModifierNode => "async",
                    IntrinsicModifierModifierNode => "intrinsic",
                    ReadonlyModifierModifierNode => "readonly",
                    _ => "other",
                });
            }
        }

        var description = parts.Count == 0 ? "<no modifiers>" : string.Join(" ", parts);
        return new ModifierListScenario(modifiers.ToImmutable(), description);
    }

    // ─── Properties ──────────────────────────────────────────────────────

    /// <summary>
    /// Property 2a: Adding sealed to a modifier list does not change the reduced Accessibility.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SealedOrthogonalityPropertyTests) })]
    public bool Sealed_DoesNotChangeReducedAccessibility(ModifierListScenario scenario)
    {
        var withoutSealed = scenario.BaseModifiers.ToList();
        var withSealed = new List<ModifierNode>(withoutSealed) { new SealedModifierModifierNode() };

        var accessWithout = AccessibilityFacts.TryReduceExplicit(withoutSealed, out var countWithout);
        var accessWith = AccessibilityFacts.TryReduceExplicit(withSealed, out var countWith);

        // The accessibility value must be the same in both cases
        return accessWithout == accessWith && countWithout == countWith;
    }

    /// <summary>
    /// Property 2b: HasSealed returns true when the sealed modifier is present.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SealedOrthogonalityPropertyTests) })]
    public bool HasSealed_ReturnsTrueWhenSealedPresent(ModifierListScenario scenario)
    {
        var withSealed = new List<ModifierNode>(scenario.BaseModifiers) { new SealedModifierModifierNode() };

        return AccessibilityFacts.HasSealed(withSealed);
    }

    /// <summary>
    /// Property 2c: HasSealed returns false when the sealed modifier is absent.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SealedOrthogonalityPropertyTests) })]
    public bool HasSealed_ReturnsFalseWhenSealedAbsent(ModifierListScenario scenario)
    {
        // The base modifiers never include sealed (by construction)
        return !AccessibilityFacts.HasSealed(scenario.BaseModifiers);
    }
}