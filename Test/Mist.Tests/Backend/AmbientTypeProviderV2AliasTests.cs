using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class AmbientTypeProviderV2AliasTests : IDisposable
{
    private readonly ProgramModelClass programModel;
    private readonly AmbientTypeProviderV2 provider;

    // No assembly is loaded: the aliases are the provider's own substitution, not anything it projects.
    public AmbientTypeProviderV2AliasTests()
    {
        programModel = new ProgramModelClass();
        AmbientProviderTestHelper.RegisterStandardTypes(programModel);

        provider = new AmbientTypeProviderV2(
            ImmutableArray<Mist.Driver.LoadedAssemblyInfo>.Empty,
            null,
            new DiagnosticBag());
        provider.BuildAndRegister(programModel);
    }

    public void Dispose()
    {
        provider.Dispose();
    }

    /// <summary>
    /// Every alias whose CLR type and Standard declaration share a simple name.
    /// </summary>
    public static TheoryData<string, string> AliasEntries => new()
    {
        { "System", "Int32" },
        { "System", "Int64" },
        { "System", "Int16" },
        { "System", "Byte" },
        { "System", "SByte" },
        { "System", "UInt32" },
        { "System", "UInt64" },
        { "System", "UInt16" },
        { "System", "Single" },
        { "System", "Double" },
        { "System", "Decimal" },
        { "System", "String" },
        { "System", "Boolean" },
        { "System", "Char" },
        { "System", "Void" },
        { "System", "Index" },
        { "System", "Range" },
        { "System.Threading.Tasks", "Task" },
        { "System.Threading.Tasks", "ValueTask" },
    };

    [Theory]
    [MemberData(nameof(AliasEntries))]
    public void AliasEntry_ProjectsAsTheReferenceEqualStandardInstance(string clrNamespace, string typeName)
    {
        var projected = provider.ProjectionOf(RuntimeType($"{clrNamespace}.{typeName}"));

        Assert.Same(programModel.TypesByFQN[$"Standard.{typeName}"], projected);
    }

    [Theory]
    [MemberData(nameof(AliasEntries))]
    public void AliasEntry_IsNamedByItsClrName(string clrNamespace, string typeName)
    {
        var named = programModel.FindQualifiedType($"{clrNamespace}.{typeName}");

        Assert.Same(programModel.TypesByFQN[$"Standard.{typeName}"], named);
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("ValueTask")]
    public void AnAliasWithAGenericSibling_ProjectsTheSiblingAsTheGenericStandardDeclaration(string typeName)
    {
        var projected = provider.ProjectionOf(RuntimeType($"System.Threading.Tasks.{typeName}`1"));

        Assert.Same(programModel.TypesByFQN[$"Standard.{typeName}<>"], projected);
        Assert.NotSame(programModel.TypesByFQN[$"Standard.{typeName}"], projected);
        Assert.Null(programModel.FindQualifiedType($"System.Threading.Tasks.{typeName}`1"));
    }

    [Fact]
    public void AliasMap_SameNameEntriesAreExactlyTheAliasEntries()
    {
        var sameName = WellKnownTypeMap.Entries
            .Where(entry => entry.Key.TypeName == entry.Value.MistTypeName)
            .Select(entry =>
            {
                Assert.Equal("Standard", entry.Value.MistNamespace);
                return (entry.Key.Namespace, entry.Key.TypeName);
            })
            .Order()
            .ToList();
        var aliasEntries = AliasEntries
            .Select(row => ((string)row[0], (string)row[1]))
            .Order()
            .ToList();

        Assert.Equal(aliasEntries, sameName);
    }

    private static Type RuntimeType(string fullName)
    {
        return typeof(object).Assembly.GetType(fullName, throwOnError: true)!;
    }
}