// Feature: method-overloading, Property 3: Multi-symbol scope lookup returns all matches
// **Validates: Requirements 1.3, 8.1**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying that <see cref="Scope.LookupAll"/> returns all
/// symbols with a given name from the scope.
///
/// Property 3: For any scope containing N symbols with the same name, calling
/// <c>LookupAll(name)</c> SHALL return a collection of exactly N symbols.
/// </summary>
public class MultiSymbolScopeLookupPropertyTests
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
    /// Represents a test scenario: N symbols sharing the same name, plus some
    /// unrelated symbols with different names to verify filtering.
    /// </summary>
#pragma warning disable CA1034
    public record ScopeLookupScenario(
        string TargetName,
        int TargetCount,
        int OtherCount)
    {
        public override string ToString()
        {
            return $"LookupAll(\"{TargetName}\") with {TargetCount} matches and {OtherCount} others";
        }
    }
#pragma warning restore CA1034

    /// <summary>
    /// Generates scope lookup scenarios with varying numbers of same-named symbols
    /// and different-named symbols.
    /// </summary>
    public static Arbitrary<ScopeLookupScenario> ArbitraryScopeLookupScenarios()
    {
        var gen = from targetCount in Gen.Choose(1, 20)
                  from otherCount in Gen.Choose(0, 10)
                  from targetName in Gen.Elements("foo", "bar", "compute", "process", "handle")
                  select new ScopeLookupScenario(targetName, targetCount, otherCount);
        return gen.ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 3: For any scope containing N symbols with the same name,
    /// <c>LookupAll(name)</c> returns exactly N symbols.
    ///
    /// We populate a scope with N method symbols sharing a target name and some
    /// additional symbols with different names, then verify the count and identity
    /// of the returned collection.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MultiSymbolScopeLookupPropertyTests) }, MaxTest = 20)]
    public bool LookupAll_ReturnsExactlyN_ForNSameNamedSymbols(ScopeLookupScenario scenario)
    {
        // Arrange: create a scope and populate it
        var scope = new Scope(parent: null);

        // Add N symbols with the target name
        var expectedSymbols = new List<ISymbol>();
        for(var i = 0; i < scenario.TargetCount; i++)
        {
            var symbol = MakeSymbol(scenario.TargetName, SymbolKind.Method, scope);
            scope.AddMember(symbol);
            expectedSymbols.Add(symbol);
        }

        // Add some symbols with different names (noise)
        for(var i = 0; i < scenario.OtherCount; i++)
        {
            var otherName = $"other_{i}";
            var symbol = MakeSymbol(otherName, SymbolKind.Method, scope);
            scope.AddMember(symbol);
        }

        // Act
        var results = scope.LookupAll(scenario.TargetName);

        // Assert: exactly N symbols returned
        if(results.Count != scenario.TargetCount)
        {
            return false;
        }

        // Assert: every returned symbol has the correct name
        if(results.Any(s => s.Name != scenario.TargetName))
        {
            return false;
        }

        // Assert: every expected symbol is present in the results
        return expectedSymbols.All(expected => results.Contains(expected));
    }

    /// <summary>
    /// Property 3 (complement): <c>LookupAll</c> for a name that does not exist
    /// in the scope returns an empty collection.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MultiSymbolScopeLookupPropertyTests) }, MaxTest = 20)]
    public bool LookupAll_ReturnsEmpty_WhenNameNotPresent(ScopeLookupScenario scenario)
    {
        // Arrange: create a scope with only "other" symbols, none matching target
        var scope = new Scope(parent: null);

        for(var i = 0; i < scenario.OtherCount; i++)
        {
            var otherName = $"other_{i}";
            scope.AddMember(MakeSymbol(otherName, SymbolKind.Field, scope));
        }

        // Act: look up a name that was never added
        var results = scope.LookupAll("nonexistent_name");

        // Assert
        return results.Count == 0;
    }

    // ─── Targeted unit-style tests ───────────────────────────────────────

    /// <summary>
    /// Verifies that a scope with a single symbol returns exactly one result
    /// from <c>LookupAll</c>.
    /// </summary>
    [Fact]
    public void LookupAll_SingleSymbol_ReturnsOne()
    {
        var scope = new Scope(parent: null);
        var symbol = MakeSymbol("foo", SymbolKind.Method, scope);
        scope.AddMember(symbol);

        var results = scope.LookupAll("foo");

        _ = Assert.Single(results);
        Assert.Same(symbol, results[0]);
    }

    /// <summary>
    /// Verifies that a scope with multiple same-named symbols (simulating method
    /// overloads) returns all of them from <c>LookupAll</c>.
    /// </summary>
    [Fact]
    public void LookupAll_MultipleOverloads_ReturnsAll()
    {
        var scope = new Scope(parent: null);
        var sym1 = MakeSymbol("compute", SymbolKind.Method, scope);
        var sym2 = MakeSymbol("compute", SymbolKind.Method, scope);
        var sym3 = MakeSymbol("compute", SymbolKind.Method, scope);
        scope.AddMember(sym1);
        scope.AddMember(sym2);
        scope.AddMember(sym3);

        var results = scope.LookupAll("compute");

        Assert.Equal(3, results.Count);
        Assert.Contains(sym1, results);
        Assert.Contains(sym2, results);
        Assert.Contains(sym3, results);
    }

    /// <summary>
    /// Verifies that <c>LookupAll</c> only returns symbols matching the requested
    /// name and does not include symbols with different names.
    /// </summary>
    [Fact]
    public void LookupAll_FiltersOutDifferentNames()
    {
        var scope = new Scope(parent: null);
        var target1 = MakeSymbol("process", SymbolKind.Method, scope);
        var target2 = MakeSymbol("process", SymbolKind.Method, scope);
        var other = MakeSymbol("handle", SymbolKind.Method, scope);
        scope.AddMember(target1);
        scope.AddMember(other);
        scope.AddMember(target2);

        var results = scope.LookupAll("process");

        Assert.Equal(2, results.Count);
        Assert.Contains(target1, results);
        Assert.Contains(target2, results);
        Assert.DoesNotContain(other, results);
    }

    /// <summary>
    /// Verifies that <c>LookupAll</c> on an empty scope returns an empty collection.
    /// </summary>
    [Fact]
    public void LookupAll_EmptyScope_ReturnsEmpty()
    {
        var scope = new Scope(parent: null);

        var results = scope.LookupAll("anything");

        Assert.Empty(results);
    }
}