// ambient-type-provider-v2, Property 6: Projection inserts into ProgramModel namespace
// ambient-type-provider-v2, Property 13: ambient assembly attribution
//
// **Validates: Requirements 2.6, 2.8, 7.1**
//
// Project the interop fixture assembly, look projected types up the way ResolveType did, then verify
// they appear in programModel.Namespaces and are attributed to the ambient assembly. No process spawning.
//
// The spec wording for Property 13 predates MistAssembly and still refers to a
// TypeOrigin.Ambient assignment; the property is unchanged, only how it is expressed.

using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.ProgramModel.Contracts.Types;
using Mist.Tests.InteropFixtures;
using Mist.Tests.InteropFixtures.ConstraintProjection;

namespace Mist.Tests.Backend;

/// <summary>
/// Verifies that projection inserts types into <c>programModel.Namespaces[ns].Types[name]</c> and
/// attributes all projected types to the <see cref="MistAssemblyKind.Ambient"/> assembly.
/// </summary>
[Trait("Tier", "2")]
public sealed class AmbientTypeProviderV2IntegrationProjectionTests(AmbientTypeProviderV2InteropFixture fixture)
    : IClassFixture<AmbientTypeProviderV2InteropFixture>
{
    private const string Fixtures = "Mist.Tests.InteropFixtures";
    private const string Constraints = "Mist.Tests.InteropFixtures.ConstraintProjection";

    private IMistType? LookupType(string namespaceName, string typeName)
    {
        // Check well-known type map first (aliases to Standard Library).
        if(WellKnownTypeMap.Entries.TryGetValue((namespaceName, typeName), out var mapping))
        {
            if(fixture.Model.TypesByFQN.TryGetValue($"{mapping.MistNamespace}.{mapping.MistTypeName}", out var stdType))
            {
                return stdType;
            }

            return null;
        }

        // Handle CLR backtick arity notation → Mist angle-bracket notation.
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

        if(fixture.Model.TypesByFQN.TryGetValue($"{namespaceName}.{mistName}", out var type))
        {
            return type;
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: Projection inserts into ProgramModel namespace
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveType_Class_InsertsIntoProgramModelNamespace()
    {
        var resolved = LookupType(Fixtures, nameof(SimpleClass));

        Assert.NotNull(resolved);
        Assert.True(fixture.Model.TypesByFQN.ContainsKey($"{Fixtures}.{nameof(SimpleClass)}"),
            "ProgramModel should have the projected class in TypesByFQN after projection.");
    }

    [Fact]
    public void ResolveType_Class_ReturnedTypeIsReferenceEqualToNamespaceEntry()
    {
        var resolved = LookupType(Fixtures, nameof(SimpleClass));

        Assert.NotNull(resolved);
        var fromFQN = fixture.Model.TypesByFQN[$"{Fixtures}.{nameof(SimpleClass)}"];

        Assert.True(
            ReferenceEquals(resolved, fromFQN),
            "ResolveType should return the same instance that was inserted into programModel.TypesByFQN.");
    }

    [Fact]
    public void ResolveType_TypeInANestedNamespace_InsertsIntoThatNamespace()
    {
        var resolved = LookupType(Constraints, nameof(Methods));

        Assert.NotNull(resolved);
        Assert.True(fixture.Model.TypesByFQN.ContainsKey($"{Constraints}.{nameof(Methods)}"));
    }

    [Fact]
    public void ResolveType_GenericBag_InsertsWithArityBearingName()
    {
        // Generic types are stored with CLR arity suffix in the provider cache
        // but registered with angle-bracket name in the namespace.
        var resolved = LookupType(Fixtures, "Bag`1");

        Assert.NotNull(resolved);
        Assert.True(fixture.Model.TypesByFQN.ContainsKey($"{Fixtures}.Bag<>"),
            "Generic Bag should be registered as 'Bag<>' in TypesByFQN.");
    }

    [Fact]
    public void ResolveType_MultipleTypes_AllInsertedIntoSameNamespace()
    {
        var simpleClass = LookupType(Fixtures, nameof(SimpleClass));
        var simpleStruct = LookupType(Fixtures, nameof(SimpleStruct));
        var simpleInterface = LookupType(Fixtures, nameof(ISimpleInterface));

        Assert.NotNull(simpleClass);
        Assert.NotNull(simpleStruct);
        Assert.NotNull(simpleInterface);

        Assert.True(fixture.Model.TypesByFQN.ContainsKey($"{Fixtures}.{nameof(SimpleClass)}"));
        Assert.True(fixture.Model.TypesByFQN.ContainsKey($"{Fixtures}.{nameof(SimpleStruct)}"));
        Assert.True(fixture.Model.TypesByFQN.ContainsKey($"{Fixtures}.{nameof(ISimpleInterface)}"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 13: ambient assembly attribution
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveType_Class_HasAmbientAssembly()
    {
        var resolved = LookupType(Fixtures, nameof(SimpleClass));

        Assert.NotNull(resolved);
        Assert.Equal(MistAssemblyKind.Ambient, resolved!.Assembly.Kind);
    }

    [Fact]
    public void ResolveType_TypeInANestedNamespace_HasAmbientAssembly()
    {
        var resolved = LookupType(Constraints, nameof(Methods));

        Assert.NotNull(resolved);
        Assert.Equal(MistAssemblyKind.Ambient, resolved!.Assembly.Kind);
    }

    [Fact]
    public void ResolveType_GenericBag_HasAmbientAssembly()
    {
        var resolved = LookupType(Fixtures, "Bag`1");

        Assert.NotNull(resolved);
        Assert.Equal(MistAssemblyKind.Ambient, resolved!.Assembly.Kind);
    }

    [Fact]
    public void ResolveType_Interface_HasAmbientAssembly()
    {
        var resolved = LookupType(Fixtures, nameof(ISimpleInterface));

        Assert.NotNull(resolved);
        Assert.Equal(MistAssemblyKind.Ambient, resolved!.Assembly.Kind);
    }

    [Fact]
    public void ResolveType_Struct_HasAmbientAssembly()
    {
        var resolved = LookupType(Fixtures, nameof(SimpleStruct));

        Assert.NotNull(resolved);
        Assert.Equal(MistAssemblyKind.Ambient, resolved!.Assembly.Kind);
    }

    [Fact]
    public void ResolveType_Enum_HasAmbientAssembly()
    {
        var resolved = LookupType(Fixtures, nameof(SimpleEnum));

        Assert.NotNull(resolved);
        Assert.Equal(MistAssemblyKind.Ambient, resolved!.Assembly.Kind);
    }

    [Fact]
    public void ResolveType_AliasedType_KeepsStandardAssembly()
    {
        // Aliased types (e.g., System.String → Standard.String) return the existing
        // Standard Library instance, so they stay attributed to the standard library
        // rather than being re-attributed as an ambient projection.
        var resolved = LookupType("System", "String");

        Assert.NotNull(resolved);
        Assert.Equal(MistAssemblyKind.Standard, resolved!.Assembly.Kind);
    }

    [Theory]
    [InlineData(Fixtures, nameof(SimpleClass))]
    [InlineData(Fixtures, nameof(SimpleStruct))]
    [InlineData(Fixtures, nameof(ISimpleInterface))]
    [InlineData(Fixtures, nameof(SimpleEnum))]
    [InlineData(Fixtures, nameof(HasNullableMembers))]
    [InlineData(Fixtures, nameof(TupleSignatures))]
    [InlineData(Constraints, nameof(Methods))]
    public void ResolveType_VariousTypes_AllHaveAmbientAssembly(string ns, string typeName)
    {
        var resolved = LookupType(ns, typeName);

        Assert.NotNull(resolved);
        Assert.Equal(MistAssemblyKind.Ambient, resolved!.Assembly.Kind);
    }

    [Theory]
    [InlineData(Fixtures, nameof(SimpleClass))]
    [InlineData(Constraints, nameof(Methods))]
    [InlineData(Fixtures, "Bag`1")]
    public void ResolveType_ProjectedType_InNamespaceIsReferenceEqual(string ns, string typeName)
    {
        var resolved = LookupType(ns, typeName);

        Assert.NotNull(resolved);

        // For generic types, the registered name uses angle-bracket notation
        var registeredName = resolved!.Name;
        var fqn = $"{ns}.{registeredName}";
        Assert.True(fixture.Model.TypesByFQN.ContainsKey(fqn),
            $"TypesByFQN should contain '{fqn}' after projection.");

        var fromFQN = fixture.Model.TypesByFQN[fqn];
        Assert.True(
            ReferenceEquals(resolved, fromFQN),
            $"ResolveType result should be reference-equal to programModel.TypesByFQN[\"{fqn}\"].");
    }
}