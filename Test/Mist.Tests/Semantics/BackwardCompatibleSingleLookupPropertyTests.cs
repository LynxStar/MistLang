// Feature: method-overloading, Property 5: Backward-compatible single lookup
// **Validates: Requirements 8.2**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying that <see cref="Scope.Lookup"/> continues to
/// return the correct single symbol when a name maps to exactly one symbol.
///
/// Property 5: For any scope where a name maps to exactly one symbol, calling
/// <c>Lookup(name)</c> SHALL return that symbol (preserving existing behavior).
/// </summary>
public class BackwardCompatibleSingleLookupPropertyTests
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
    /// Represents a test scenario: a single symbol with a target name, plus some
    /// unrelated symbols with different names to verify filtering.
    /// </summary>
#pragma warning disable CA1034
    public record SingleLookupScenario(
        string TargetName,
        SymbolKind TargetKind,
        int OtherCount)
    {
        public override string ToString()
        {
            return $"Lookup(\"{TargetName}\") [{TargetKind}] with {OtherCount} other symbols";
        }
    }
#pragma warning restore CA1034

    /// <summary>
    /// Generates single lookup scenarios with varying symbol kinds and numbers
    /// of unrelated symbols.
    /// </summary>
    public static Arbitrary<SingleLookupScenario> ArbitrarySingleLookupScenarios()
    {
        var gen = from targetName in Gen.Elements("foo", "bar", "compute", "process", "handle", "value", "count")
                  from targetKind in Gen.Elements(
                      SymbolKind.Method,
                      SymbolKind.Field,
                      SymbolKind.Property,
                      SymbolKind.LocalVariable,
                      SymbolKind.Class,
                      SymbolKind.Parameter)
                  from otherCount in Gen.Choose(0, 10)
                  select new SingleLookupScenario(targetName, targetKind, otherCount);
        return gen.ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 5: For any scope where a name maps to exactly one symbol,
    /// <c>Lookup(name)</c> returns that exact symbol.
    ///
    /// We populate a scope with exactly one symbol having the target name and
    /// some additional symbols with different names, then verify that Lookup
    /// returns the correct symbol by reference identity.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(BackwardCompatibleSingleLookupPropertyTests) }, MaxTest = 20)]
    public bool Lookup_ReturnsSingleSymbol_WhenExactlyOneExists(SingleLookupScenario scenario)
    {
        // Arrange: create a scope with exactly one target symbol
        var scope = new Scope(parent: null);

        var targetSymbol = MakeSymbol(scenario.TargetName, scenario.TargetKind, scope);
        scope.AddMember(targetSymbol);

        // Add unrelated symbols with different names
        for(var i = 0; i < scenario.OtherCount; i++)
        {
            var otherName = $"other_{i}";
            scope.AddMember(MakeSymbol(otherName, SymbolKind.Field, scope));
        }

        // Act
        var result = scope.Lookup(scenario.TargetName);

        // Assert: Lookup returns the exact symbol (reference equality)
        return ReferenceEquals(result, targetSymbol);
    }

    /// <summary>
    /// Property 5 (scope chain variant): For any scope chain where a name maps
    /// to exactly one symbol in an ancestor scope, <c>Lookup(name)</c> on the
    /// innermost scope returns that symbol via parent traversal.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(BackwardCompatibleSingleLookupPropertyTests) }, MaxTest = 20)]
    public bool Lookup_FindsSingleSymbolInAncestor_ViaParentChain(SingleLookupScenario scenario)
    {
        // Arrange: create a parent scope with the target symbol
        var parent = new Scope(parent: null);
        var targetSymbol = MakeSymbol(scenario.TargetName, scenario.TargetKind, parent);
        parent.AddMember(targetSymbol);

        // Create a child scope with only unrelated symbols
        var child = new Scope(parent: parent);
        for(var i = 0; i < scenario.OtherCount; i++)
        {
            var otherName = $"other_{i}";
            child.AddMember(MakeSymbol(otherName, SymbolKind.Field, child));
        }

        // Act: Lookup on the child scope
        var result = child.Lookup(scenario.TargetName);

        // Assert: Lookup returns the exact symbol from the parent
        return ReferenceEquals(result, targetSymbol);
    }

    /// <summary>
    /// Property 5 (consistency): For any scope where a name maps to exactly one
    /// symbol, <c>Lookup(name)</c> and <c>LookupAll(name)</c> agree — Lookup
    /// returns the same symbol that LookupAll returns as its single element.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(BackwardCompatibleSingleLookupPropertyTests) }, MaxTest = 20)]
    public bool Lookup_ConsistentWithLookupAll_ForSingleSymbol(SingleLookupScenario scenario)
    {
        // Arrange
        var scope = new Scope(parent: null);
        var targetSymbol = MakeSymbol(scenario.TargetName, scenario.TargetKind, scope);
        scope.AddMember(targetSymbol);

        for(var i = 0; i < scenario.OtherCount; i++)
        {
            scope.AddMember(MakeSymbol($"other_{i}", SymbolKind.Field, scope));
        }

        // Act
        var lookupResult = scope.Lookup(scenario.TargetName);
        var lookupAllResults = scope.LookupAll(scenario.TargetName);

        // Assert: LookupAll returns exactly one, and it's the same as Lookup
        return lookupAllResults.Count == 1
            && ReferenceEquals(lookupResult, lookupAllResults[0]);
    }

    // ─── Targeted unit-style tests ───────────────────────────────────────

    /// <summary>
    /// Verifies that Lookup returns the single symbol when only one exists.
    /// </summary>
    [Fact]
    public void Lookup_SingleMethod_ReturnsIt()
    {
        var scope = new Scope(parent: null);
        var symbol = MakeSymbol("foo", SymbolKind.Method, scope);
        scope.AddMember(symbol);

        var result = scope.Lookup("foo");

        Assert.NotNull(result);
        Assert.Same(symbol, result);
    }

    /// <summary>
    /// Verifies that Lookup returns null when the name does not exist.
    /// </summary>
    [Fact]
    public void Lookup_NonexistentName_ReturnsNull()
    {
        var scope = new Scope(parent: null);
        scope.AddMember(MakeSymbol("foo", SymbolKind.Method, scope));

        var result = scope.Lookup("bar");

        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that Lookup finds a single symbol in a parent scope when the
    /// child scope has no matching symbols.
    /// </summary>
    [Fact]
    public void Lookup_SingleSymbolInParent_FoundFromChild()
    {
        var parent = new Scope(parent: null);
        var symbol = MakeSymbol("compute", SymbolKind.Method, parent);
        parent.AddMember(symbol);

        var child = new Scope(parent: parent);
        child.AddMember(MakeSymbol("other", SymbolKind.Field, child));

        var result = child.Lookup("compute");

        Assert.NotNull(result);
        Assert.Same(symbol, result);
    }

    /// <summary>
    /// Verifies that Lookup returns the correct symbol even when the scope
    /// contains many unrelated symbols.
    /// </summary>
    [Fact]
    public void Lookup_SingleAmongManyOthers_ReturnsCorrectSymbol()
    {
        var scope = new Scope(parent: null);

        // Add many unrelated symbols
        for(var i = 0; i < 20; i++)
        {
            scope.AddMember(MakeSymbol($"other_{i}", SymbolKind.Field, scope));
        }

        // Add the single target symbol
        var target = MakeSymbol("target", SymbolKind.Property, scope);
        scope.AddMember(target);

        var result = scope.Lookup("target");

        Assert.NotNull(result);
        Assert.Same(target, result);
    }
}