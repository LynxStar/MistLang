// Feature: assembly-type-provider, Property 11: Type Resolution Caching Identity
//
// **Validates: Requirements 3.4**
//
// For any CLR type resolved through the AssemblyTypeProvider, subsequent resolutions
// of the same type (identified by namespace, name, and generic arity) SHALL return
// the same IType instance (reference equality).

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based test for Type Resolution Caching Identity (Property 11).
/// Resolves the same type multiple times through <see cref="AssemblyTypeProvider"/>
/// and verifies reference equality — the same <c>IType</c> instance is returned each time.
///
/// **Validates: Requirements 3.4**
/// </summary>
public sealed class TypeResolutionCachingIdentityPropertyTests : IDisposable
{
    private readonly AssemblyTypeProvider _provider;

    /// <summary>
    /// All resolvable (namespace, typeName) pairs from the runtime assemblies
    /// and the InteropFixtures assembly.
    /// </summary>
    private static readonly (string Namespace, string TypeName)[] ResolvableTypes = BuildResolvableTypes();

    public TypeResolutionCachingIdentityPropertyTests()
    {
        var assemblyPaths = GetAssemblyPaths();
        var diagnostics = new DiagnosticBag();
        _provider = new AssemblyTypeProvider(assemblyPaths, diagnostics);
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    private static ImmutableArray<string> GetAssemblyPaths()
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimePaths = Directory.GetFiles(runtimeDir, "*.dll").ToImmutableArray();

        // Include the InteropFixtures assembly
        var fixtureAssemblyPath = typeof(InteropFixtures.SimpleClass).Assembly.Location;
        return runtimePaths.Add(fixtureAssemblyPath);
    }

    private static (string Namespace, string TypeName)[] BuildResolvableTypes()
    {
        var results = new List<(string, string)>();
        var filter = new VisibilityFilter();

        // Collect types from the InteropFixtures assembly
        var fixtureAssembly = typeof(InteropFixtures.SimpleClass).Assembly;
        foreach(var type in fixtureAssembly.GetExportedTypes())
        {
            if(filter.IsTypeExcluded(type))
                continue;

            var ns = type.Namespace;
            if(ns is null)
                continue;

            // Use the name as the provider would see it (with generic arity suffix stripped)
            var name = TypeProjector.StripGenericAritySuffix(type.Name);
            results.Add((ns, name));
        }

        // Collect a selection of well-known runtime types to increase diversity
        var runtimeTypes = new[]
        {
            typeof(object), typeof(string), typeof(int), typeof(double),
            typeof(List<>), typeof(Dictionary<,>), typeof(HashSet<>),
            typeof(IEnumerable<>), typeof(IList<>), typeof(IDisposable),
            typeof(Exception), typeof(ArgumentException), typeof(InvalidOperationException),
            typeof(Console), typeof(Math), typeof(Convert),
            typeof(Tuple<>), typeof(Tuple<,>), typeof(Task<>),
            typeof(Nullable<>), typeof(Action), typeof(Func<>),
            typeof(StringComparison), typeof(DateTimeKind),
        };

        foreach(var type in runtimeTypes)
        {
            if(filter.IsTypeExcluded(type))
                continue;

            var ns = type.Namespace;
            if(ns is null)
                continue;

            var name = TypeProjector.StripGenericAritySuffix(type.Name);
            results.Add((ns, name));
        }

        return results.Distinct().ToArray();
    }

    /// <summary>
    /// Generates a random index into the ResolvableTypes array.
    /// </summary>
    private static Gen<int> GenTypeIndex()
    {
        return Gen.Choose(0, ResolvableTypes.Length - 1);
    }

    /// <summary>
    /// Generates a random number of resolution attempts (2–5) to verify caching.
    /// </summary>
    private static Gen<int> GenResolutionCount()
    {
        return Gen.Choose(2, 5);
    }

    private static void RunProperty(Gen<(int TypeIndex, int ResolutionCount)> gen, Action<(int, int)> check, int maxTests = 100)
    {
        var samples = gen.Sample(maxTests).ToList();
        for(var idx = 0; idx < samples.Count; idx++)
        {
            try
            {
                check(samples[idx]);
            }
            catch(Exception ex)
            {
                var (typeIdx, count) = samples[idx];
                var (ns, name) = ResolvableTypes[typeIdx];
                throw new Exception(
                    $"Property failed on sample {idx}/{samples.Count}: " +
                    $"type=({ns}, {name}), resolutionCount={count}\n" +
                    $"Inner: {ex.Message}", ex);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 11: Type Resolution Caching Identity
    //
    // For any CLR type resolved through the AssemblyTypeProvider, subsequent
    // resolutions of the same type SHALL return the same IType instance
    // (reference equality).
    //
    // **Validates: Requirements 3.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void P11_TypeResolutionCachingIdentity_ReturnsReferenceEqualInstances()
    {
        var gen = GenTypeIndex().SelectMany(typeIdx =>
            GenResolutionCount().Select(count => (typeIdx, count)));

        RunProperty(gen, scenario =>
        {
            var (typeIdx, resolutionCount) = scenario;
            var (ns, typeName) = ResolvableTypes[typeIdx];

            // First resolution
            var firstResult = _provider.ResolveType(ns, typeName);

            // Skip types that don't resolve (e.g., intrinsic types that get substituted)
            if(firstResult is null)
                return;

            // Resolve the same type multiple times and verify reference equality
            for(var i = 1; i < resolutionCount; i++)
            {
                var subsequentResult = _provider.ResolveType(ns, typeName);

                Assert.True(
                    ReferenceEquals(firstResult, subsequentResult),
                    $"Resolution #{i + 1} of ({ns}, {typeName}) returned a different IType instance. " +
                    $"Expected reference equality (same cached instance).");
            }
        });
    }
}