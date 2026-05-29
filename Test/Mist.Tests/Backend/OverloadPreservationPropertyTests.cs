// Feature: assembly-type-provider, Property 12: Overload Preservation
// For any CLR type with multiple public method overloads sharing the same name,
// the Assembly_Type_Provider SHALL project all overloads that pass the visibility
// filter as separate Mist method symbols. Filtering one overload SHALL NOT affect
// the projection of other overloads.
// **Validates: Requirements 11.1, 11.2**

using System.Reflection;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for overload preservation in <see cref="TypeProjector"/>.
/// Validates that filtering one overload does not affect projection of others.
/// </summary>
public sealed class OverloadPreservationPropertyTests
{
    private static readonly TypeProjector Projector = CreateProjector();
    private static readonly VisibilityFilter Filter = new();
    private static readonly IStdlibTypeProvider Stdlib = FallbackStdlibTypeProvider.Instance;

    private static TypeProjector CreateProjector()
    {
        var intrinsicMap = new IntrinsicTypeMap();
        return new TypeProjector(intrinsicMap, new VisibilityFilter());
    }

    // ─── Types with overloaded methods from the fixture assembly ─────────

    /// <summary>
    /// All fixture assembly types that have at least one overloaded method name
    /// (multiple public methods with the same name).
    /// </summary>
    private static readonly Type[] TypesWithOverloads = typeof(HasOverloads).Assembly
        .GetExportedTypes()
        .Where(t => !Filter.IsTypeExcluded(t))
        .Where(HasOverloadedMethods)
        .ToArray();

    private static bool HasOverloadedMethods(Type type)
    {
        var publicMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => !m.IsSpecialName)
            .ToArray();

        return publicMethods
            .GroupBy(m => m.Name)
            .Any(g => g.Count() > 1);
    }

    // ─── Scenario type ───────────────────────────────────────────────────

    /// <summary>
    /// Represents a randomly selected type with overloaded methods and a
    /// randomly selected overload group (method name) to verify.
    /// </summary>
#pragma warning disable CA1034
    public sealed class OverloadScenario
    {
        public Type ClrType { get; }
        public string MethodName { get; }

        public OverloadScenario(Type clrType, string methodName)
        {
            ClrType = clrType;
            MethodName = methodName;
        }

        public override string ToString() => $"{ClrType.Name}.{MethodName}";
    }
#pragma warning restore CA1034

    // ─── Pre-computed overload groups per type ─────────────────────────────

    private static readonly (Type Type, string[] OverloadNames)[] TypeOverloadPairs =
        TypesWithOverloads.Select(t => (
            Type: t,
            OverloadNames: t
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => !m.IsSpecialName)
                .GroupBy(m => m.Name)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray()
        )).ToArray();

    // ─── Arbitrary ───────────────────────────────────────────────────────

    public static Arbitrary<OverloadScenario> ArbitraryOverloadScenario()
    {
        var gen = Gen.Choose(0, TypeOverloadPairs.Length - 1).SelectMany(typeIdx =>
        {
            var (type, overloadNames) = TypeOverloadPairs[typeIdx];
            return Gen.Choose(0, overloadNames.Length - 1)
                .Select(nameIdx => new OverloadScenario(type, overloadNames[nameIdx]));
        });

        return gen.ToArbitrary();
    }

    // ─── Property Tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 12a: For any type with overloaded methods, the number of projected
    /// method symbols with a given name equals the number of overloads that pass
    /// the visibility filter.
    /// **Validates: Requirements 11.1, 11.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OverloadPreservationPropertyTests) })]
    public bool ProjectedOverloadCount_MatchesVisibleOverloadCount(OverloadScenario scenario)
    {
        // Count how many overloads pass the visibility filter
        var allOverloads = scenario.ClrType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => !m.IsSpecialName && m.Name == scenario.MethodName)
            .ToArray();

        var visibleOverloads = allOverloads
            .Where(m => !Filter.IsMemberExcluded(m))
            .ToArray();

        // Project the type and its members
        var (projectedType, _) = Projector.ProjectType(scenario.ClrType, Stdlib);
        var members = Projector.ProjectMembers(scenario.ClrType, projectedType, Stdlib);

        // Count projected method symbols with the same name
        var projectedOverloads = members
            .Where(m => m.Kind == SymbolKind.Method && m.Name == scenario.MethodName)
            .ToArray();

        return projectedOverloads.Length == visibleOverloads.Length;
    }

    /// <summary>
    /// Property 12b: For any type with overloaded methods where at least one overload
    /// is filtered (e.g., has out param), the remaining overloads are still projected
    /// independently — their count is strictly less than the total overload count but
    /// greater than zero.
    /// **Validates: Requirements 11.1, 11.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OverloadPreservationPropertyTests) })]
    public bool FilteredOverloads_DoNotAffectRemainingProjection(OverloadScenario scenario)
    {
        var allOverloads = scenario.ClrType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => !m.IsSpecialName && m.Name == scenario.MethodName)
            .ToArray();

        var visibleOverloads = allOverloads
            .Where(m => !Filter.IsMemberExcluded(m))
            .ToArray();

        var filteredOverloads = allOverloads
            .Where(m => Filter.IsMemberExcluded(m))
            .ToArray();

        // Project the type and its members
        var (projectedType, _) = Projector.ProjectType(scenario.ClrType, Stdlib);
        var members = Projector.ProjectMembers(scenario.ClrType, projectedType, Stdlib);

        var projectedOverloads = members
            .Where(m => m.Kind == SymbolKind.Method && m.Name == scenario.MethodName)
            .ToArray();

        // If some overloads are filtered, the projected count should be less than total
        // but still equal to the visible count (filtering doesn't suppress others)
        if (filteredOverloads.Length > 0)
        {
            return projectedOverloads.Length == visibleOverloads.Length
                && projectedOverloads.Length < allOverloads.Length
                && projectedOverloads.Length > 0;
        }

        // If no overloads are filtered, all should be projected
        return projectedOverloads.Length == allOverloads.Length;
    }

    /// <summary>
    /// Property 12c: Each projected overload has a distinct parameter signature —
    /// no two projected overloads for the same method name have identical parameter
    /// type lists.
    /// **Validates: Requirements 11.1, 11.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OverloadPreservationPropertyTests) })]
    public bool ProjectedOverloads_HaveDistinctSignatures(OverloadScenario scenario)
    {
        var (projectedType, _) = Projector.ProjectType(scenario.ClrType, Stdlib);
        var members = Projector.ProjectMembers(scenario.ClrType, projectedType, Stdlib);

        var projectedOverloads = members
            .Where(m => m.Kind == SymbolKind.Method && m.Name == scenario.MethodName)
            .Cast<Symbol>()
            .ToArray();

        // Each overload should have a unique parameter signature
        // (parameter type display names joined)
        var signatures = projectedOverloads
            .Select(m => string.Join(",",
                m.Parameters.Select(p => p.DeclaredType?.DisplayName ?? "?")))
            .ToArray();

        var distinctSignatures = signatures.Distinct().Count();
        return distinctSignatures == projectedOverloads.Length;
    }
}
