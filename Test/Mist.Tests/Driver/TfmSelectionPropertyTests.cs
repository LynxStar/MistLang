// Feature: assembly-type-provider, Property 15: NuGet TFM Selection
// **Validates: Requirements 9.5**
//
// For any NuGet package targeting multiple frameworks, the Package_Resolver SHALL
// select the nearest compatible target framework moniker at or below net10.0,
// preferring net10.0 > net9.0 > net8.0 > netstandard2.1 > netstandard2.0.

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Driver;

namespace Mist.Tests.Driver;

/// <summary>
/// Property-based tests for TFM selection in <see cref="PackageResolver"/>.
/// Verifies that the nearest compatible TFM is selected per the preference order:
/// net10.0 > net9.0 > net8.0 > netstandard2.1 > netstandard2.0.
/// All tests are pure in-process — no process spawning.
/// </summary>
public sealed class TfmSelectionPropertyTests
{
    // The known TFMs in preference order (highest preference first)
    private static readonly string[] KnownTfms =
    [
        "net10.0",
        "net9.0",
        "net8.0",
        "netstandard2.1",
        "netstandard2.0",
    ];

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents a test scenario: a non-empty subset of TFMs available in a package.
    /// </summary>
#pragma warning disable CA1034
#pragma warning disable CA1819 // FsCheck record properties require concrete array types for generation
    public record TfmSelectionScenario(string[] AvailableTfms)
    {
        public override string ToString() =>
            $"Available: [{string.Join(", ", AvailableTfms)}]";
    }
#pragma warning restore CA1819
#pragma warning restore CA1034

    /// <summary>
    /// Generates non-empty subsets of the known TFM list.
    /// Each scenario represents a package that targets some subset of the known frameworks.
    /// </summary>
    public static Arbitrary<TfmSelectionScenario> ArbitraryTfmSelectionScenario()
    {
        // Generate a bitmask (1–31) to select a non-empty subset of the 5 known TFMs
        var gen = Gen.Choose(1, (1 << KnownTfms.Length) - 1)
            .Select(mask => new TfmSelectionScenario(
                KnownTfms
                    .Where((_, i) => (mask & (1 << i)) != 0)
                    .ToArray()));

        return gen.ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Determines the expected best TFM from a list of available TFMs
    /// according to the preference order: net10.0 > net9.0 > net8.0 > netstandard2.1 > netstandard2.0.
    /// Returns the TFM with the lowest index in the preference order.
    /// </summary>
    private static string GetExpectedBestTfm(string[] availableTfms)
    {
        // The first TFM in KnownTfms that appears in availableTfms is the best
        foreach (var tfm in KnownTfms)
        {
            if (availableTfms.Contains(tfm))
                return tfm;
        }

        // Should not happen with our generator (always non-empty subset of KnownTfms)
        throw new InvalidOperationException("No compatible TFM found in available list.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property tests
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 15: NuGet TFM Selection.
    /// For any non-empty subset of known TFMs, GetTfmPreferenceIndex assigns the
    /// lowest index (highest preference) to the TFM that should be selected.
    /// The selected TFM must be the one with the highest preference in the available set.
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(TfmSelectionPropertyTests) })]
    public bool TfmSelection_SelectsNearestCompatiblePerPreferenceOrder(TfmSelectionScenario scenario)
    {
        var expectedBest = GetExpectedBestTfm(scenario.AvailableTfms);

        // Simulate the selection logic: find the TFM with the lowest preference index
        string? bestTfm = null;
        var bestPreferenceIndex = int.MaxValue;

        foreach (var tfmStr in scenario.AvailableTfms)
        {
            var preferenceIndex = PackageResolver.GetTfmPreferenceIndex(tfmStr);

            if (preferenceIndex < bestPreferenceIndex)
            {
                bestPreferenceIndex = preferenceIndex;
                bestTfm = tfmStr;
            }
        }

        return bestTfm == expectedBest;
    }

    /// <summary>
    /// Property 15 (ordering invariant): For any two TFMs where one has higher
    /// preference than the other, GetTfmPreferenceIndex assigns a strictly lower
    /// index to the higher-preference TFM.
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(TfmSelectionPropertyTests) })]
    public bool TfmPreferenceIndex_RespectsDefinedOrder(TfmSelectionScenario scenario)
    {
        // For all pairs in the available TFMs, verify ordering is consistent
        for (var i = 0; i < scenario.AvailableTfms.Length; i++)
        {
            for (var j = i + 1; j < scenario.AvailableTfms.Length; j++)
            {
                var tfmA = scenario.AvailableTfms[i];
                var tfmB = scenario.AvailableTfms[j];

                var indexA = Array.IndexOf(KnownTfms, tfmA);
                var indexB = Array.IndexOf(KnownTfms, tfmB);

                var prefA = PackageResolver.GetTfmPreferenceIndex(tfmA);
                var prefB = PackageResolver.GetTfmPreferenceIndex(tfmB);

                // If A has higher preference (lower index in KnownTfms),
                // then it should have a lower preference index from GetTfmPreferenceIndex
                if (indexA < indexB && prefA >= prefB)
                    return false;
                if (indexA > indexB && prefA <= prefB)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Property 15 (unknown TFM handling): TFMs not in the preference list
    /// get int.MaxValue (lowest priority), ensuring known TFMs are always preferred.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TfmSelectionPropertyTests) })]
    public bool UnknownTfm_GetsLowestPriority(TfmSelectionScenario scenario)
    {
        // An unknown TFM (e.g., net7.0) should get int.MaxValue
        var unknownIndex = PackageResolver.GetTfmPreferenceIndex("net7.0");

        if (unknownIndex != int.MaxValue)
            return false;

        // Every known TFM in the scenario should have a lower (better) preference index
        foreach (var tfmStr in scenario.AvailableTfms)
        {
            var knownIndex = PackageResolver.GetTfmPreferenceIndex(tfmStr);

            if (knownIndex >= unknownIndex)
                return false;
        }

        return true;
    }
}
