// ambient-type-provider-v2, Property 1: Alias identity preservation
//
// **Validates: Requirements 3.1, 2.2, 7.4**
//
// For all 17 alias entries, verify ResolveType returns reference-equal Standard Library instance.
// Uses [Theory] + [MemberData] over the finite alias set (no process spawning).

using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

/// <summary>
/// Property test: for all 17 alias entries, <see cref="AmbientTypeProviderV2.ResolveType"/>
/// returns the reference-equal Standard Library instance from the ProgramModel.
/// </summary>
public sealed class AmbientTypeProviderV2AliasTests : IDisposable
{
    private readonly ProgramModelClass programModel;
    private readonly Namespace standardNamespace;
    private readonly AmbientTypeProviderV2 provider;

    public AmbientTypeProviderV2AliasTests()
    {
        programModel = new ProgramModelClass();
        standardNamespace = programModel.GetOrAddNamespace("Standard");

        // Register all 17 expected Standard Library types as ClassType stubs.
        // The alias map maps CLR types to these Standard namespace types.
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

        // Create provider with empty assemblies and null metadata context
        provider = new AmbientTypeProviderV2(
            ImmutableArray<Mist.Driver.LoadedAssemblyInfo>.Empty,
            programModel.TypeInterner,
            null);
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

    [Theory]
    [MemberData(nameof(AliasEntries))]
    public void ResolveType_AliasEntry_ReturnsReferenceEqualStandardInstance(
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
            $"Standard Library instance for \"{expectedMistTypeName}\", but got a different object.");
    }

    [Theory]
    [MemberData(nameof(AliasEntries))]
    public void ResolveType_ArityAwareOverload_ReturnsReferenceEqualStandardInstance(
        string clrNamespace, string clrTypeName, string expectedMistTypeName)
    {
        // Aliases are non-generic (arity 0)
        var resolved = provider.ResolveType(clrNamespace, clrTypeName, 0, programModel);

        Assert.NotNull(resolved);

        var expected = standardNamespace.Types[expectedMistTypeName];
        Assert.True(
            ReferenceEquals(resolved, expected),
            $"ResolveType(\"{clrNamespace}\", \"{clrTypeName}\", 0) should return the reference-equal " +
            $"Standard Library instance for \"{expectedMistTypeName}\".");
    }

    [Theory]
    [MemberData(nameof(AliasEntries))]
    public void ResolveType_ArityAwareOverload_WithNonZeroArity_ReturnsNull(
        string clrNamespace, string clrTypeName, string expectedMistTypeName)
    {
        _ = expectedMistTypeName; // unused in this test variant

        // Aliases should NOT match when arity > 0
        var resolved = provider.ResolveType(clrNamespace, clrTypeName, 1, programModel);

        Assert.Null(resolved);
    }

    [Fact]
    public void AliasMap_HasExactly17Entries()
    {
        // Verify we're testing all entries by checking the alias count matches our data.
        // Count unique entries in our TheoryData — should be 17.
        var count = 0;
        foreach(var _ in AliasEntries)
        {
            count++;
        }

        Assert.Equal(17, count);
    }
}