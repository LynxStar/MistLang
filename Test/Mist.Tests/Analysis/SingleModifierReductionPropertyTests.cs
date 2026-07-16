// Feature: full-accessibility-model, Property 1: Single access modifier reduces to that accessibility
// **Validates: Requirements 2.1, 2.2, 2.3, 2.4**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Generated;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for single-modifier reduction in <see cref="AccessibilityFacts"/>.
///
/// Property 1: For any declaration whose only access modifier is exactly one of
/// <c>public</c>, <c>private</c>, <c>protected</c>, or <c>internal</c>, the reduced
/// Accessibility value equals that modifier's corresponding enum value.
///
/// Exercises <see cref="AccessibilityFacts.TryReduceExplicit"/> directly (in-process,
/// no process spawn) with generated single-access-modifier lists that may also contain
/// zero or more non-access modifiers (static, readonly, sealed, intrinsic, async).
///
/// **Validates: Requirements 2.1, 2.2, 2.3, 2.4**
/// </summary>
public class SingleModifierReductionPropertyTests
{
    // ─── Non-access modifiers that can accompany an access modifier ──────

    private static readonly ModifierNode[] NonAccessModifiers =
    [
        new StaticModifierModifierNode(),
        new ReadonlyModifierModifierNode(),
        new SealedModifierModifierNode(),
        new IntrinsicModifierModifierNode(),
        new AsyncModifierModifierNode(),
    ];

    // ─── Scenario type ───────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A generated scenario containing exactly one access modifier and zero or more
    /// non-access modifiers, shuffled into a random order.
    /// </summary>
    public sealed record SingleAccessModifierScenario(
        IReadOnlyList<ModifierNode> Modifiers,
        Accessibility ExpectedAccessibility)
    {
        public override string ToString()
        {
            var modNames = Modifiers.Select(m => m.GetType().Name.Replace("ModifierModifierNode", "")).ToList();
            return $"[{string.Join(", ", modNames)}] -> {ExpectedAccessibility}";
        }
    }

#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<SingleAccessModifierScenario> GenSingleAccessModifierScenario()
    {
        return from accessIndex in Gen.Choose(0, 3)
               from nonAccessCount in Gen.Choose(0, NonAccessModifiers.Length)
               from shuffled in Gen.Shuffle(NonAccessModifiers)
               let accessModifier = CreateAccessModifier(accessIndex)
               let expected = (Accessibility)accessIndex
               let combined = shuffled.Take(nonAccessCount).Prepend(accessModifier).ToArray()
               from finalOrder in Gen.Shuffle(combined)
               select new SingleAccessModifierScenario(finalOrder.ToList(), expected);
    }

    private static ModifierNode CreateAccessModifier(int index)
    {
        return index switch
        {
            0 => new PublicModifierModifierNode(),
            1 => new PrivateModifierModifierNode(),
            2 => new ProtectedModifierModifierNode(),
            3 => new InternalModifierModifierNode(),
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<SingleAccessModifierScenario> ArbitrarySingleAccessModifierScenario()
    {
        return GenSingleAccessModifierScenario().ToArbitrary();
    }

    // ─── Property ────────────────────────────────────────────────────────

    /// <summary>
    /// Property 1: Single access modifier reduces to that accessibility.
    ///
    /// For any modifier list containing exactly one access modifier (plus zero or
    /// more non-access modifiers in any order), <see cref="AccessibilityFacts.TryReduceExplicit"/>
    /// returns the corresponding <see cref="Accessibility"/> value with
    /// <c>accessModifierCount == 1</c>.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SingleModifierReductionPropertyTests) })]
    public Property SingleAccessModifier_ReducesToExpectedAccessibility(
        SingleAccessModifierScenario scenario)
    {
        var result = AccessibilityFacts.TryReduceExplicit(
            scenario.Modifiers, out var accessModifierCount);

        var resultMatchesExpected = result == scenario.ExpectedAccessibility;
        var countIsOne = accessModifierCount == 1;

        return (resultMatchesExpected && countIsOne).ToProperty()
            .Label($"Expected {scenario.ExpectedAccessibility} with count=1, " +
                   $"got {result} with count={accessModifierCount}. " +
                   $"Modifiers: {scenario}");
    }
}