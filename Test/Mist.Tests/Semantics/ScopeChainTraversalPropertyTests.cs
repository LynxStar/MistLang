// Feature: method-overloading, Property 4: Scope chain traversal collects from ancestors
// **Validates: Requirements 8.3**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying that <see cref="Scope.LookupAll"/> traverses
/// the full parent chain and collects symbols from all ancestor scopes.
///
/// Property 4: For any nested scope chain where symbols with the same name exist
/// at multiple levels, calling <c>LookupAll(name)</c> on the innermost scope SHALL
/// return symbols from the current scope and all ancestor scopes.
/// </summary>
public class ScopeChainTraversalPropertyTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="Symbol"/> with the given name and kind, belonging to the
    /// specified scope. Uses minimal defaults for fields not relevant to lookup.
    /// </summary>
    private static Symbol MakeSymbol(string name, SymbolKind kind, IScope containingScope)
    {
        return new Symbol(
            name: name,
            kind: kind,
            origin: SymbolOrigin.UserCode,
            containingScope: containingScope,
            declaredType: null,
            declarationSpan: null);
    }

    // ─── FsCheck generators ─────────────────────────────────────────────

    /// <summary>
    /// Represents a test scenario: a chain of scopes (2-5 levels deep) where each
    /// level has a specified number of symbols with the target name.
    /// </summary>
#pragma warning disable CA1034
    public record ScopeChainScenario(
        string TargetName,
        IReadOnlyList<int> SymbolCountsPerLevel)
    {
        public int ChainDepth => SymbolCountsPerLevel.Count;
        public int TotalExpected => SymbolCountsPerLevel.Sum();

        public override string ToString() =>
            $"LookupAll(\"{TargetName}\") across {ChainDepth} levels: [{string.Join(", ", SymbolCountsPerLevel)}] = {TotalExpected} total";
    }
#pragma warning restore CA1034

    /// <summary>
    /// Generates scope chain scenarios with 2-5 levels, each level having 1-5
    /// symbols with the target name.
    /// </summary>
    public static Arbitrary<ScopeChainScenario> ArbitraryScopeChainScenarios()
    {
        var gen = from depth in Gen.Choose(2, 5)
                  from counts in GenCounts(depth)
                  from targetName in Gen.Elements("foo", "bar", "compute", "process", "handle")
                  select new ScopeChainScenario(targetName, counts);
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates a list of exactly <paramref name="count"/> integers, each between 1 and 5.
    /// </summary>
    private static Gen<IReadOnlyList<int>> GenCounts(int count)
    {
        return Gen.ArrayOf(Gen.Choose(1, 5), count)
                  .Select(arr => (IReadOnlyList<int>)arr.ToList());
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 4: For any nested scope chain where symbols with the same name
    /// exist at multiple levels, <c>LookupAll(name)</c> on the innermost scope
    /// returns symbols from ALL levels combined.
    ///
    /// We build a chain of scopes (root → … → innermost), add symbols with the
    /// target name at each level, then verify the total count and identity of
    /// the returned collection from the innermost scope.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ScopeChainTraversalPropertyTests) }, MaxTest = 20)]
    public bool LookupAll_CollectsFromAllAncestorScopes(ScopeChainScenario scenario)
    {
        // Arrange: build the scope chain from root to innermost
        var allExpectedSymbols = new List<ISymbol>();
        Scope? currentScope = null;

        for(var level = 0; level < scenario.ChainDepth; level++)
        {
            currentScope = new Scope(parent: currentScope);

            for(var i = 0; i < scenario.SymbolCountsPerLevel[level]; i++)
            {
                var symbol = MakeSymbol(scenario.TargetName, SymbolKind.Method, currentScope);
                currentScope.AddMember(symbol);
                allExpectedSymbols.Add(symbol);
            }
        }

        // Act: LookupAll on the innermost scope
        var results = currentScope!.LookupAll(scenario.TargetName);

        // Assert: total count matches sum across all levels
        if(results.Count != scenario.TotalExpected)
            return false;

        // Assert: every returned symbol has the correct name
        if(results.Any(s => s.Name != scenario.TargetName))
            return false;

        // Assert: every expected symbol is present in the results
        return allExpectedSymbols.All(expected => results.Contains(expected));
    }

    /// <summary>
    /// Property 4 (complement): When only some levels in the chain contain
    /// symbols with the target name, <c>LookupAll</c> still collects from
    /// all levels that have matches and skips levels that don't.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ScopeChainTraversalPropertyTests) }, MaxTest = 20)]
    public bool LookupAll_CollectsOnlyFromLevelsWithMatches(ScopeChainScenario scenario)
    {
        // Arrange: build the scope chain, but only add target symbols at
        // even-indexed levels; odd levels get unrelated symbols only.
        var allExpectedSymbols = new List<ISymbol>();
        Scope? currentScope = null;
        var expectedCount = 0;

        for(var level = 0; level < scenario.ChainDepth; level++)
        {
            currentScope = new Scope(parent: currentScope);

            if(level % 2 == 0)
            {
                // Add target symbols at even levels
                for(var i = 0; i < scenario.SymbolCountsPerLevel[level]; i++)
                {
                    var symbol = MakeSymbol(scenario.TargetName, SymbolKind.Method, currentScope);
                    currentScope.AddMember(symbol);
                    allExpectedSymbols.Add(symbol);
                    expectedCount++;
                }
            }
            else
            {
                // Add unrelated symbols at odd levels
                for(var i = 0; i < scenario.SymbolCountsPerLevel[level]; i++)
                {
                    var otherSymbol = MakeSymbol($"unrelated_{level}_{i}", SymbolKind.Field, currentScope);
                    currentScope.AddMember(otherSymbol);
                }
            }
        }

        // Act
        var results = currentScope!.LookupAll(scenario.TargetName);

        // Assert: count matches only the even-level symbols
        if(results.Count != expectedCount)
            return false;

        // Assert: every returned symbol has the correct name
        if(results.Any(s => s.Name != scenario.TargetName))
            return false;

        // Assert: every expected symbol is present
        return allExpectedSymbols.All(expected => results.Contains(expected));
    }

    // ─── Targeted unit-style tests ───────────────────────────────────────

    /// <summary>
    /// Verifies that a two-level scope chain collects symbols from both levels.
    /// </summary>
    [Fact]
    public void LookupAll_TwoLevelChain_CollectsFromBoth()
    {
        var parent = new Scope(parent: null);
        var parentSym = MakeSymbol("foo", SymbolKind.Method, parent);
        parent.AddMember(parentSym);

        var child = new Scope(parent: parent);
        var childSym = MakeSymbol("foo", SymbolKind.Method, child);
        child.AddMember(childSym);

        var results = child.LookupAll("foo");

        Assert.Equal(2, results.Count);
        Assert.Contains(childSym, results);
        Assert.Contains(parentSym, results);
    }

    /// <summary>
    /// Verifies that a three-level scope chain collects symbols from all levels.
    /// </summary>
    [Fact]
    public void LookupAll_ThreeLevelChain_CollectsFromAll()
    {
        var grandparent = new Scope(parent: null);
        var gpSym = MakeSymbol("bar", SymbolKind.Method, grandparent);
        grandparent.AddMember(gpSym);

        var parent = new Scope(parent: grandparent);
        var pSym = MakeSymbol("bar", SymbolKind.Method, parent);
        parent.AddMember(pSym);

        var child = new Scope(parent: parent);
        var cSym = MakeSymbol("bar", SymbolKind.Method, child);
        child.AddMember(cSym);

        var results = child.LookupAll("bar");

        Assert.Equal(3, results.Count);
        Assert.Contains(gpSym, results);
        Assert.Contains(pSym, results);
        Assert.Contains(cSym, results);
    }

    /// <summary>
    /// Verifies that when only the parent has symbols with the target name,
    /// <c>LookupAll</c> on the child still finds them.
    /// </summary>
    [Fact]
    public void LookupAll_OnlyParentHasSymbols_ChildFindsThemViaTraversal()
    {
        var parent = new Scope(parent: null);
        var sym1 = MakeSymbol("compute", SymbolKind.Method, parent);
        var sym2 = MakeSymbol("compute", SymbolKind.Method, parent);
        parent.AddMember(sym1);
        parent.AddMember(sym2);

        var child = new Scope(parent: parent);
        // child has no "compute" symbols

        var results = child.LookupAll("compute");

        Assert.Equal(2, results.Count);
        Assert.Contains(sym1, results);
        Assert.Contains(sym2, results);
    }

    /// <summary>
    /// Verifies that unrelated symbols in ancestor scopes are not included
    /// in the results.
    /// </summary>
    [Fact]
    public void LookupAll_ChainWithMixedNames_OnlyReturnsMatches()
    {
        var parent = new Scope(parent: null);
        var parentTarget = MakeSymbol("process", SymbolKind.Method, parent);
        var parentOther = MakeSymbol("handle", SymbolKind.Method, parent);
        parent.AddMember(parentTarget);
        parent.AddMember(parentOther);

        var child = new Scope(parent: parent);
        var childTarget = MakeSymbol("process", SymbolKind.Method, child);
        child.AddMember(childTarget);

        var results = child.LookupAll("process");

        Assert.Equal(2, results.Count);
        Assert.Contains(parentTarget, results);
        Assert.Contains(childTarget, results);
        Assert.DoesNotContain(parentOther, results);
    }
}