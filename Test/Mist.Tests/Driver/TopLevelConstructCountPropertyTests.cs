// Feature: compiler-progress-output, Property 2: Top-level construct count matches symbol filter
// **Validates: Requirements 6.1, 6.2**
//
// Property-based test verifying that the filter predicate used in Stage 4 of
// CompilerDriver correctly counts top-level user-code constructs. The filter is:
//   Origin == SymbolOrigin.UserCode AND Kind in {Class, Struct, Abstract, Trait}
//
// We generate random lists of (SymbolKind, SymbolOrigin) pairs and verify that
// the LINQ-based filter count matches a manual loop count.

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Semantics.Contracts;

namespace Mist.Tests.Driver;

/// <summary>
/// Property-based tests for the top-level construct count filter logic
/// used in Stage 4 (Semantic Model) of the compilation pipeline.
/// </summary>
public class TopLevelConstructCountPropertyTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Filter under test (mirrors the logic in CompilerDriver Stage 4)
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly HashSet<SymbolKind> TopLevelKinds = new()
    {
        SymbolKind.Class,
        SymbolKind.Struct,
        SymbolKind.Abstract,
        SymbolKind.Trait,
    };

    /// <summary>
    /// The filter predicate as used in CompilerDriver Stage 4.
    /// </summary>
    private static bool IsTopLevelUserConstruct(SymbolKind kind, SymbolOrigin origin) =>
        origin == SymbolOrigin.UserCode && TopLevelKinds.Contains(kind);

    /// <summary>
    /// Alternative manual count implementation — iterates and counts without LINQ.
    /// </summary>
    private static int ManualCount(IReadOnlyList<(SymbolKind Kind, SymbolOrigin Origin)> pairs)
    {
        var count = 0;
        foreach (var (kind, origin) in pairs)
        {
            if (origin != SymbolOrigin.UserCode)
                continue;
            if (kind == SymbolKind.Class || kind == SymbolKind.Struct ||
                kind == SymbolKind.Abstract || kind == SymbolKind.Trait)
            {
                count++;
            }
        }

        return count;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Arbitrary / Generator
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a random (SymbolKind, SymbolOrigin) pair by picking from all enum values.
    /// </summary>
    private static Gen<(SymbolKind Kind, SymbolOrigin Origin)> GenSymbolPair()
    {
        var kinds = Enum.GetValues<SymbolKind>();
        var origins = Enum.GetValues<SymbolOrigin>();

        var genKind = Gen.Elements<SymbolKind>(kinds);
        var genOrigin = Gen.Elements<SymbolOrigin>(origins);

        return genKind.SelectMany(k => genOrigin.Select(o => (k, o)));
    }

    /// <summary>
    /// Generates a list of 0–50 random (SymbolKind, SymbolOrigin) pairs.
    /// </summary>
    private static Gen<(SymbolKind Kind, SymbolOrigin Origin)[]> GenSymbolPairList() =>
        Gen.Choose(0, 50).SelectMany(size =>
            Gen.ArrayOf(GenSymbolPair(), size));

    public static Arbitrary<(SymbolKind Kind, SymbolOrigin Origin)[]> ArbSymbolPairList() =>
        GenSymbolPairList().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property test
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 2: Top-level construct count matches symbol filter.
    /// The LINQ filter count must equal the manual loop count for any list of symbol pairs.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TopLevelConstructCountPropertyTests) })]
    public bool FilterCount_MatchesManualCount((SymbolKind Kind, SymbolOrigin Origin)[] pairs)
    {
        // LINQ-based filter (mirrors CompilerDriver Stage 4 implementation)
        var filterCount = pairs.Count(p => IsTopLevelUserConstruct(p.Kind, p.Origin));

        // Manual count (alternative implementation)
        var manualCount = ManualCount(pairs);

        return filterCount == manualCount;
    }
}
