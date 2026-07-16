// Feature: full-accessibility-model, Property 3: Multiple access modifiers are rejected
//
// **Validates: Requirements 2.6**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Generated;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying that multiple access modifiers are rejected.
///
/// Property 3: For any modifier list containing two or more distinct access modifiers,
/// the reduction reports a multiple-access-modifier error (accessModifierCount > 1).
///
/// Exercises <see cref="AccessibilityFacts.TryReduceExplicit"/> directly (in-process,
/// no process spawn) with generated lists containing 2+ access modifiers plus optional
/// non-access modifiers.
///
/// **Validates: Requirements 2.6**
/// </summary>
public class MultipleAccessModifierRejectionPropertyTests
{
    // ─── Modifier pools ──────────────────────────────────────────────────

    private static readonly ModifierNode[] AccessModifiers =
    [
        new PublicModifierModifierNode(),
        new PrivateModifierModifierNode(),
        new ProtectedModifierModifierNode(),
        new InternalModifierModifierNode(),
    ];

    private static readonly ModifierNode[] NonAccessModifiers =
    [
        new StaticModifierModifierNode(),
        new AsyncModifierModifierNode(),
        new IntrinsicModifierModifierNode(),
        new ReadonlyModifierModifierNode(),
        new SealedModifierModifierNode(),
    ];

    // ─── Scenario type ───────────────────────────────────────────────────

#pragma warning disable CA1034
    /// <summary>
    /// A generated scenario containing two or more distinct access modifiers and
    /// zero or more non-access modifiers, shuffled into a random order.
    /// </summary>
    public sealed class MultipleAccessModifierScenario
    {
        public IReadOnlyList<ModifierNode> Modifiers { get; }
        public int ExpectedAccessModifierCount { get; }
        public string Description { get; }

        public MultipleAccessModifierScenario(
            IReadOnlyList<ModifierNode> modifiers,
            int expectedAccessModifierCount,
            string description)
        {
            Modifiers = modifiers;
            ExpectedAccessModifierCount = expectedAccessModifierCount;
            Description = description;
        }

        public override string ToString()
        {
            return Description;
        }
    }
#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<MultipleAccessModifierScenario> GenMultipleAccessModifierScenario()
    {
        // Pick 2, 3, or 4 distinct access modifiers (indices 0..3)
        return from accessCount in Gen.Choose(2, 4)
               from shuffledAccess in Gen.Shuffle(AccessModifiers)
               from nonAccessMask in Gen.Choose(0, (1 << NonAccessModifiers.Length) - 1)
               from finalOrder in Gen.Shuffle(
                   BuildModifierList(shuffledAccess.Take(accessCount).ToArray(), nonAccessMask))
               let description = BuildDescription(
                   shuffledAccess.Take(accessCount).ToArray(), nonAccessMask)
               select new MultipleAccessModifierScenario(
                   finalOrder.ToList(), accessCount, description);
    }

    private static ModifierNode[] BuildModifierList(ModifierNode[] accessMods, int nonAccessMask)
    {
        var result = new List<ModifierNode>(accessMods);

        for(var i = 0; i < NonAccessModifiers.Length; i++)
        {
            if((nonAccessMask & (1 << i)) != 0)
            {
                result.Add(NonAccessModifiers[i]);
            }
        }

        return result.ToArray();
    }

    private static string BuildDescription(ModifierNode[] accessMods, int nonAccessMask)
    {
        var parts = new List<string>();

        foreach(var mod in accessMods)
        {
            parts.Add(mod switch
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
                parts.Add(NonAccessModifiers[i] switch
                {
                    StaticModifierModifierNode => "static",
                    AsyncModifierModifierNode => "async",
                    IntrinsicModifierModifierNode => "intrinsic",
                    ReadonlyModifierModifierNode => "readonly",
                    SealedModifierModifierNode => "sealed",
                    _ => "other",
                });
            }
        }

        return string.Join(" ", parts);
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<MultipleAccessModifierScenario> ArbitraryMultipleAccessModifierScenario()
    {
        return GenMultipleAccessModifierScenario().ToArbitrary();
    }

    // ─── Property ────────────────────────────────────────────────────────

    /// <summary>
    /// Property 3: Multiple access modifiers are rejected.
    ///
    /// For any modifier list containing two or more distinct access modifiers (plus
    /// zero or more non-access modifiers in any order),
    /// <see cref="AccessibilityFacts.TryReduceExplicit"/> reports
    /// <c>accessModifierCount > 1</c>, confirming the multiple-access-modifier
    /// condition is detected.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MultipleAccessModifierRejectionPropertyTests) })]
    public Property MultipleAccessModifiers_AreRejected(MultipleAccessModifierScenario scenario)
    {
        _ = AccessibilityFacts.TryReduceExplicit(
            scenario.Modifiers, out var accessModifierCount);

        var countExceedsOne = accessModifierCount > 1;
        var countMatchesExpected = accessModifierCount == scenario.ExpectedAccessModifierCount;

        return (countExceedsOne && countMatchesExpected).ToProperty()
            .Label($"Expected accessModifierCount={scenario.ExpectedAccessModifierCount} (>1), " +
                   $"got {accessModifierCount}. " +
                   $"Modifiers: {scenario}");
    }
}