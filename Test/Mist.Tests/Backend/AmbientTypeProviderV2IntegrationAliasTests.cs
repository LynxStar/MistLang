// ambient-type-provider-v2, Property 5: Resolution idempotence and reference equality
//
// **Validates: Requirements 6.3, 6.4**
//
// Load real BCL assemblies via MetadataLoadContext.
// Verify all 17 alias entries resolve to Standard Library instances.
// Verify repeated ResolveType calls return same reference.
// NO process spawning.

using System.Collections.Immutable;
using System.Reflection;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

/// <summary>
/// Integration test: verifies alias resolution and reference equality when
/// real BCL assemblies are loaded via <see cref="MetadataLoadContext"/>.
/// Unlike <see cref="AmbientTypeProviderV2AliasTests"/> (which uses empty assemblies),
/// this test loads the full runtime to confirm aliases short-circuit before the
/// namespace index and that repeated resolution is idempotent.
/// </summary>
public sealed class AmbientTypeProviderV2IntegrationAliasTests : IDisposable
{
    private readonly ProgramModelClass programModel;
    private readonly Namespace standardNamespace;
    private readonly MetadataLoadContext metadataContext;
    private readonly AmbientTypeProviderV2 provider;

    public AmbientTypeProviderV2IntegrationAliasTests()
    {
        programModel = new ProgramModelClass();
        standardNamespace = programModel.GetOrAddNamespace("Standard");

        // Register all 17 expected Standard Library types as ClassType stubs.
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

        // Build LoadedAssemblyInfo for each assembly that loads successfully.
        var loaded = ImmutableArray.CreateBuilder<LoadedAssemblyInfo>();
        foreach(var path in assemblyPaths)
        {
            try
            {
                var assembly = metadataContext.LoadFromAssemblyPath(path);
                loaded.Add(new LoadedAssemblyInfo(path, assembly));
            }
            catch
            {
                // Skip assemblies that fail to load (same as production code).
            }
        }

        provider = new AmbientTypeProviderV2(
            loaded.ToImmutable(),
            programModel.TypeInterner,
            metadataContext);
    }

    public void Dispose()
    {
        provider.Dispose();
    }

    /// <summary>
    /// All 17 alias entries: (CLR namespace, CLR type name, expected Mist type name in Standard).
    /// </summary>
    public static TheoryData<string, string, string> AliasEntries
    {
        get
        {
            var data = new TheoryData<string, string, string>
            {
                // 11 numeric types
                { "System", "Int32", "Int32" },
                { "System", "Int64", "Int64" },
                { "System", "Int16", "Int16" },
                { "System", "Byte", "Byte" },
                { "System", "SByte", "SByte" },
                { "System", "UInt32", "UInt32" },
                { "System", "UInt64", "UInt64" },
                { "System", "UInt16", "UInt16" },
                { "System", "Single", "Single" },
                { "System", "Double", "Double" },
                { "System", "Decimal", "Decimal" },
                // Primitives
                { "System", "String", "String" },
                { "System", "Boolean", "Boolean" },
                { "System", "Char", "Char" },
                { "System", "Void", "Void" },
                // Async
                { "System.Threading.Tasks", "Task", "Task" },
                { "System.Threading.Tasks", "ValueTask", "ValueTask" },
            };

            return data;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Alias resolution with real assemblies loaded (Req 6.3, 3.1)
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(AliasEntries))]
    public void ResolveType_WithRealAssembliesLoaded_ReturnsReferenceEqualStandardInstance(
        string clrNamespace, string clrTypeName, string expectedMistTypeName)
    {
        // Act
        var resolved = provider.ResolveType(clrNamespace, clrTypeName, programModel);

        // Assert - resolved is not null
        Assert.NotNull(resolved);

        // Assert - resolved is the exact same instance registered in Standard namespace
        var expected = standardNamespace.Types[expectedMistTypeName];
        Assert.True(
            ReferenceEquals(resolved, expected),
            $"ResolveType(\"{clrNamespace}\", \"{clrTypeName}\") should return the reference-equal " +
            $"Standard Library instance for \"{expectedMistTypeName}\" even with real assemblies loaded.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Repeated resolution returns same reference (Req 6.3, 6.4)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveType_CalledTwiceForSameNonAliasedType_ReturnsSameReference()
    {
        // Arrange - use a well-known BCL type that is NOT aliased
        var namespaceName = "System.Collections.Generic";
        var typeName = "List`1";

        // Act - resolve twice
        var first = provider.ResolveType(namespaceName, typeName, programModel);
        var second = provider.ResolveType(namespaceName, typeName, programModel);

        // Assert - both calls return non-null and reference-equal
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(
            ReferenceEquals(first, second),
            "Repeated ResolveType calls for the same type should return the same reference.");
    }

    [Fact]
    public void ResolveType_CalledTwiceForSameNonAliasedType_ArityOverload_ReturnsSameReference()
    {
        // Arrange - use the arity-aware overload for Dictionary`2
        var namespaceName = "System.Collections.Generic";
        var typeName = "Dictionary";

        // Act - resolve twice with arity
        var first = provider.ResolveType(namespaceName, typeName, 2, programModel);
        var second = provider.ResolveType(namespaceName, typeName, 2, programModel);

        // Assert - both calls return non-null and reference-equal
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(
            ReferenceEquals(first, second),
            "Repeated arity-aware ResolveType calls for the same type should return the same reference.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Non-aliased BCL type resolution (Req 6.2)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveType_ForBclType_ReturnsNonNullProjectedType()
    {
        // Arrange - a well-known non-aliased BCL type
        var namespaceName = "System.Collections.Generic";
        var typeName = "List`1";

        // Act
        var resolved = provider.ResolveType(namespaceName, typeName, programModel);

        // Assert
        Assert.NotNull(resolved);
        Assert.Equal("List<>", resolved!.Name);
    }

    [Fact]
    public void ResolveType_AliasedType_IsNotReprojectedFromAssembly()
    {
        // Verify that resolving "System.Int32" returns the Standard Library instance,
        // NOT a freshly-projected type from the loaded System.Runtime assembly.
        var resolved = provider.ResolveType("System", "Int32", programModel);
        var standardInt32 = standardNamespace.Types["Int32"];

        // The resolved type should be the EXACT Standard Library stub,
        // not a newly projected ClassType with full members.
        Assert.True(
            ReferenceEquals(resolved, standardInt32),
            "Aliased types should return the Standard Library instance, not a projected assembly type.");

        // Additionally verify no new "Int32" was inserted into the "System" namespace
        // (because the alias map short-circuits before projection)
        if(programModel.Namespaces.TryGetValue("System", out var systemNs))
        {
            if(systemNs.Types.TryGetValue("Int32", out var systemInt32))
            {
                // If something is in System.Int32, it should still be reference-equal
                // to Standard.Int32 (not a duplicate projection)
                Assert.True(
                    ReferenceEquals(systemInt32, standardInt32),
                    "If Int32 exists in System namespace, it should be the same Standard Library instance.");
            }
        }
    }
}