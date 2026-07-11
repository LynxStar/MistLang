// ambient-type-provider-v2, Property 5: Resolution idempotence and reference equality (concurrent variant)
//
// **Validates: Requirements 6.3, 6.4**
//
// Load real BCL assemblies via MetadataLoadContext.
// Spawn parallel tasks resolving same type concurrently.
// Verify all receive the same reference-equal instance.
// NO process spawning. Use Task.Run for in-process parallelism.

using System.Collections.Immutable;
using System.Reflection;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

/// <summary>
/// Concurrency test: verifies that parallel resolution produces the same
/// reference-equal instance for both same-type and different-type scenarios.
/// Uses real BCL assemblies loaded via <see cref="MetadataLoadContext"/>.
/// </summary>
public sealed class AmbientTypeProviderV2ConcurrencyTests : IDisposable
{
    private readonly ProgramModelClass programModel;
    private readonly MetadataLoadContext metadataContext;
    private readonly AmbientTypeProviderV2 provider;

    public AmbientTypeProviderV2ConcurrencyTests()
    {
        programModel = new ProgramModelClass();
        var standardNamespace = programModel.GetOrAddNamespace("Standard");

        // Register Standard Library types required for alias resolution.
        var typeNames = new[]
        {
            "Int32", "Int64", "Int16", "Byte", "SByte",
            "UInt32", "UInt64", "UInt16", "Single", "Double", "Decimal",
            "String", "Boolean", "Char", "Void",
            "Task", "ValueTask"
        };

        var scope = new Scope(null);
        foreach(var name in typeNames)
        {
            var type = new ClassType(name, "Standard", scope, null, null!);
            standardNamespace.Types[name] = type;
        }

        // Load real BCL assemblies via MetadataLoadContext
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var assemblyPaths = Directory.GetFiles(runtimeDir, "*.dll").ToImmutableArray();

        var resolver = new PathAssemblyResolver(assemblyPaths);
        metadataContext = new MetadataLoadContext(resolver);

        var loaded = AmbientTypeProviderV2Factory.LoadAssemblies(assemblyPaths, metadataContext, new DiagnosticBag());

        provider = new AmbientTypeProviderV2(
            loaded,
            programModel.TypeInterner,
            metadataContext);
    }

    public void Dispose()
    {
        provider.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 1: Parallel ResolveType for the SAME type → all get same reference
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveType_ConcurrentAccess_AllReceiveSameReference()
    {
        const int parallelism = 10;
        var results = new IMistType?[parallelism];

        // Spawn parallel tasks all resolving the same type
        var tasks = Enumerable.Range(0, parallelism).Select(i => Task.Run(() =>
        {
            results[i] = provider.ResolveType("System", "Console", programModel);
        }));

        await Task.WhenAll(tasks);

        // All results should be non-null
        Assert.All(results, Assert.NotNull);

        // All results should be reference-equal
        var first = results[0];
        for(var i = 1; i < parallelism; i++)
        {
            Assert.True(ReferenceEquals(first, results[i]),
                $"Result {i} should be reference-equal to result 0");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 2: Parallel ResolveType for DIFFERENT types → each gets distinct reference
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveType_ConcurrentAccessDifferentTypes_EachGetsDistinctReference()
    {
        // Use well-known BCL types from different namespaces
        var typeRequests = new (string Namespace, string TypeName)[]
        {
            ("System", "Console"),
            ("System", "Math"),
            ("System", "Environment"),
            ("System", "GC"),
            ("System.IO", "File"),
        };

        var results = new IMistType?[typeRequests.Length];

        // Spawn parallel tasks each resolving a different type
        var tasks = Enumerable.Range(0, typeRequests.Length).Select(i => Task.Run(() =>
        {
            var (ns, name) = typeRequests[i];
            results[i] = provider.ResolveType(ns, name, programModel);
        }));

        await Task.WhenAll(tasks);

        // All results should be non-null
        Assert.All(results, Assert.NotNull);

        // Each result should be distinct (not reference-equal to others)
        for(var i = 0; i < results.Length; i++)
        {
            for(var j = i + 1; j < results.Length; j++)
            {
                Assert.False(ReferenceEquals(results[i], results[j]),
                    $"Result {i} ({typeRequests[i].TypeName}) should NOT be reference-equal " +
                    $"to result {j} ({typeRequests[j].TypeName})");
            }
        }
    }
}