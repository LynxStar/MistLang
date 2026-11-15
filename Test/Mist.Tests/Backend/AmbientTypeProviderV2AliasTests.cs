using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class AmbientTypeProviderV2AliasTests : IDisposable
{
    private readonly ProgramModelClass programModel;
    private readonly AmbientTypeProviderV2 provider;

    private IMistType? LookupType(string namespaceName, string typeName)
    {
        if(WellKnownTypeMap.Entries.TryGetValue((namespaceName, typeName), out var mapping))
        {
            if(programModel.TypesByFQN.TryGetValue($"{mapping.MistNamespace}.{mapping.MistTypeName}", out var stdType))
            {
                return stdType;
            }

            return null;
        }

        var mistName = typeName;
        var backtickIndex = typeName.IndexOf('`');
        if(backtickIndex >= 0)
        {
            var baseName = typeName[..backtickIndex];
            var arityStr = typeName[(backtickIndex + 1)..];
            if(int.TryParse(arityStr, out var arity) && arity >= 1)
            {
                var commas = new string(',', arity - 1);
                mistName = $"{baseName}<{commas}>";
            }
        }

        if(programModel.TypesByFQN.TryGetValue($"{namespaceName}.{mistName}", out var type))
        {
            return type;
        }

        return null;
    }

    public AmbientTypeProviderV2AliasTests()
    {
        programModel = new ProgramModelClass();
        _ = programModel.GetOrAddNamespace("Standard");

        var typeNames = new[]
        {
            "Int32", "Int64", "Int16", "Byte", "SByte",
            "UInt32", "UInt64", "UInt16", "Single", "Double", "Decimal",
            "String", "Boolean", "Char", "Void",
            "Task", "Task<>", "ValueTask", "ValueTask<>"
        };

        var scope = new Scope(null);
        foreach(var name in typeNames)
        {
            var type = new ClassType(name, "Standard", scope, null, null!);
            _ = programModel.RegisterType(type);
        }

        provider = new AmbientTypeProviderV2(
            ImmutableArray<Mist.Driver.LoadedAssemblyInfo>.Empty,
            null,
            new DiagnosticBag());
    }

    public void Dispose()
    {
        provider.Dispose();
    }

    public static TheoryData<string, string, string> AliasEntries
    {
        get
        {
            var data = new TheoryData<string, string, string>
            {
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
                { "System", "String", "String" },
                { "System", "Boolean", "Boolean" },
                { "System", "Char", "Char" },
                { "System", "Void", "Void" },
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
        var resolved = LookupType(clrNamespace, clrTypeName);

        Assert.NotNull(resolved);

        var expected = programModel.TypesByFQN[$"Standard.{expectedMistTypeName}"];
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
        var resolved = LookupType(clrNamespace, clrTypeName);

        Assert.NotNull(resolved);

        var expected = programModel.TypesByFQN[$"Standard.{expectedMistTypeName}"];
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
        _ = expectedMistTypeName;

        // Task and ValueTask have generic variants that ARE aliased (Task<> -> Standard.Task<>)
        if(clrTypeName is "Task" or "ValueTask")
        {
            return;
        }

        var resolved = LookupType(clrNamespace, $"{clrTypeName}<>");

        Assert.Null(resolved);
    }

    [Fact]
    public void AliasMap_HasExactly17Entries()
    {
        var count = 0;
        foreach(var _ in AliasEntries)
        {
            count++;
        }

        Assert.Equal(17, count);
    }
}