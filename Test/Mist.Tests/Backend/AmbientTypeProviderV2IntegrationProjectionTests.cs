// ambient-type-provider-v2, Property 6: Projection inserts into ProgramModel namespace
// ambient-type-provider-v2, Property 13: TypeOrigin.Ambient assignment
//
// **Validates: Requirements 2.6, 2.8, 7.1**
//
// Load BCL assemblies, create AmbientTypeProviderV2, call ResolveType for known BCL types,
// then verify projected types appear in programModel.Namespaces and carry TypeOrigin.Ambient.
// No process spawning.

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
/// Integration tests verifying that <see cref="AmbientTypeProviderV2.ResolveType"/>
/// inserts projected types into <c>programModel.Namespaces[ns].Types[name]</c> and
/// assigns <see cref="TypeOrigin.Ambient"/> to all projected types.
/// </summary>
public sealed class AmbientTypeProviderV2IntegrationProjectionTests : IDisposable
{
    private readonly ProgramModelClass programModel;
    private readonly AmbientTypeProviderV2 provider;
    private readonly MetadataLoadContext metadataContext;

    public AmbientTypeProviderV2IntegrationProjectionTests()
    {
        programModel = new ProgramModelClass();
        var stdNs = programModel.GetOrAddNamespace("Standard");
        var scope = new Scope(null);

        // Register Standard Library types needed for alias resolution and projections.
        var standardTypes = new[]
        {
            "Int32", "Int64", "Int16", "Byte", "SByte",
            "UInt32", "UInt64", "UInt16", "Single", "Double", "Decimal",
            "String", "Boolean", "Char", "Void",
            "Task", "Task<>", "ValueTask", "ValueTask<>"
        };

        foreach(var name in standardTypes)
        {
            var type = new ClassType(name, "Standard", scope, null, null!);
            stdNs.Types[name] = type;
        }

        // Register Standard.IEnumerable<> and Standard.IEnumerator<> for interface mapping.
        var ienumerable = new AbstractType("IEnumerable<>", "Standard", scope, null, null);
        var ienumerator = new AbstractType("IEnumerator<>", "Standard", scope, null, null);
        stdNs.Types["IEnumerable<>"] = ienumerable;
        stdNs.Types["IEnumerator<>"] = ienumerator;

        // Register Standard.Func definitions for delegate projection.
        for(var arity = 1; arity <= 17; arity++)
        {
            var commas = arity > 1 ? new string(',', arity - 1) : "";
            var funcName = $"Func<{commas}>";
            var funcType = new ClassType(funcName, "Standard", scope, null, null!);
            stdNs.Types[funcName] = funcType;
        }

        // Load real BCL assemblies via MetadataLoadContext.
        var runtimePath = typeof(object).Assembly.Location;
        var runtimeDir = Path.GetDirectoryName(runtimePath)!;
        var assemblyPaths = Directory.GetFiles(runtimeDir, "*.dll").ToImmutableArray();

        var resolver = new PathAssemblyResolver(assemblyPaths);
        metadataContext = new MetadataLoadContext(resolver);

        var loadedAssemblies = AmbientTypeProviderV2Factory.LoadAssemblies(assemblyPaths, metadataContext, new DiagnosticBag());

        provider = new AmbientTypeProviderV2(loadedAssemblies, programModel.TypeInterner, null);
    }

    public void Dispose()
    {
        provider.Dispose();
        metadataContext.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: Projection inserts into ProgramModel namespace
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveType_Console_InsertsIntoProgramModelNamespace()
    {
        // Act
        var resolved = provider.ResolveType("System", "Console", programModel);

        // Assert
        Assert.NotNull(resolved);
        Assert.True(programModel.Namespaces.ContainsKey("System"),
            "ProgramModel should have a 'System' namespace after resolving System.Console.");

        var systemNs = programModel.Namespaces["System"];
        Assert.True(systemNs.Types.ContainsKey("Console"),
            "System namespace should contain 'Console' type after projection.");
    }

    [Fact]
    public void ResolveType_Console_ReturnedTypeIsReferenceEqualToNamespaceEntry()
    {
        // Act
        var resolved = provider.ResolveType("System", "Console", programModel);

        // Assert
        Assert.NotNull(resolved);
        var systemNs = programModel.Namespaces["System"];
        var fromNamespace = systemNs.Types["Console"];

        Assert.True(
            ReferenceEquals(resolved, fromNamespace),
            "ResolveType should return the same instance that was inserted into programModel.Namespaces.");
    }

    [Fact]
    public void ResolveType_MemoryStream_InsertsIntoProgramModelNamespace()
    {
        // Act
        var resolved = provider.ResolveType("System.IO", "MemoryStream", programModel);

        // Assert
        Assert.NotNull(resolved);
        Assert.True(programModel.Namespaces.ContainsKey("System.IO"));

        var ioNs = programModel.Namespaces["System.IO"];
        Assert.True(ioNs.Types.ContainsKey("MemoryStream"));
    }

    [Fact]
    public void ResolveType_GenericList_InsertsWithArityBearingName()
    {
        // Act — generic types are stored with CLR arity suffix in the provider cache
        // but registered with angle-bracket name in the namespace
        var resolved = provider.ResolveType("System.Collections.Generic", "List`1", programModel);

        // Assert
        Assert.NotNull(resolved);
        Assert.True(programModel.Namespaces.ContainsKey("System.Collections.Generic"));

        var collNs = programModel.Namespaces["System.Collections.Generic"];
        // Generic types are registered with angle-bracket notation
        Assert.True(collNs.Types.ContainsKey("List<>"),
            "Generic List should be registered as 'List<>' in ProgramModel namespace.");
    }

    [Fact]
    public void ResolveType_MultipleTypes_AllInsertedIntoSameNamespace()
    {
        // Act — resolve multiple types from the same namespace
        var console = provider.ResolveType("System", "Console", programModel);
        var environment = provider.ResolveType("System", "Environment", programModel);
        var math = provider.ResolveType("System", "Math", programModel);

        // Assert
        Assert.NotNull(console);
        Assert.NotNull(environment);
        Assert.NotNull(math);

        var systemNs = programModel.Namespaces["System"];
        Assert.True(systemNs.Types.ContainsKey("Console"));
        Assert.True(systemNs.Types.ContainsKey("Environment"));
        Assert.True(systemNs.Types.ContainsKey("Math"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 13: TypeOrigin.Ambient assignment
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveType_Console_HasTypeOriginAssembly()
    {
        // Act
        var resolved = provider.ResolveType("System", "Console", programModel);

        // Assert
        Assert.NotNull(resolved);
        Assert.Equal(TypeOrigin.Ambient, resolved!.Origin);
    }

    [Fact]
    public void ResolveType_MemoryStream_HasTypeOriginAssembly()
    {
        var resolved = provider.ResolveType("System.IO", "MemoryStream", programModel);

        Assert.NotNull(resolved);
        Assert.Equal(TypeOrigin.Ambient, resolved!.Origin);
    }

    [Fact]
    public void ResolveType_GenericList_HasTypeOriginAssembly()
    {
        var resolved = provider.ResolveType("System.Collections.Generic", "List`1", programModel);

        Assert.NotNull(resolved);
        Assert.Equal(TypeOrigin.Ambient, resolved!.Origin);
    }

    [Fact]
    public void ResolveType_Interface_HasTypeOriginAssembly()
    {
        var resolved = provider.ResolveType("System", "IDisposable", programModel);

        Assert.NotNull(resolved);
        Assert.Equal(TypeOrigin.Ambient, resolved!.Origin);
    }

    [Fact]
    public void ResolveType_Struct_HasTypeOriginAssembly()
    {
        var resolved = provider.ResolveType("System", "DateTime", programModel);

        Assert.NotNull(resolved);
        Assert.Equal(TypeOrigin.Ambient, resolved!.Origin);
    }

    [Fact]
    public void ResolveType_Enum_HasTypeOriginAssembly()
    {
        var resolved = provider.ResolveType("System", "DayOfWeek", programModel);

        Assert.NotNull(resolved);
        Assert.Equal(TypeOrigin.Ambient, resolved!.Origin);
    }

    [Fact]
    public void ResolveType_AliasedType_HasTypeOriginSource()
    {
        // Aliased types (e.g., System.String → Standard.String) retain Source origin
        // because they return the existing Standard Library instance.
        var resolved = provider.ResolveType("System", "String", programModel);

        Assert.NotNull(resolved);
        Assert.Equal(TypeOrigin.Source, resolved!.Origin);
    }

    [Theory]
    [InlineData("System", "Console")]
    [InlineData("System", "Math")]
    [InlineData("System", "Environment")]
    [InlineData("System.IO", "MemoryStream")]
    [InlineData("System.IO", "StreamReader")]
    [InlineData("System", "DateTime")]
    [InlineData("System", "DayOfWeek")]
    public void ResolveType_VariousTypes_AllHaveTypeOriginAssembly(string ns, string typeName)
    {
        var resolved = provider.ResolveType(ns, typeName, programModel);

        Assert.NotNull(resolved);
        Assert.Equal(TypeOrigin.Ambient, resolved!.Origin);
    }

    [Theory]
    [InlineData("System", "Console")]
    [InlineData("System.IO", "MemoryStream")]
    [InlineData("System.Collections.Generic", "List`1")]
    public void ResolveType_ProjectedType_InNamespaceIsReferenceEqual(string ns, string typeName)
    {
        // Act
        var resolved = provider.ResolveType(ns, typeName, programModel);

        // Assert
        Assert.NotNull(resolved);
        Assert.True(programModel.Namespaces.ContainsKey(ns));

        var namespaceDictionary = programModel.Namespaces[ns];
        // For generic types, the registered name uses angle-bracket notation
        var registeredName = resolved!.Name;
        Assert.True(namespaceDictionary.Types.ContainsKey(registeredName),
            $"Namespace '{ns}' should contain type '{registeredName}' after projection.");

        var fromNamespace = namespaceDictionary.Types[registeredName];
        Assert.True(
            ReferenceEquals(resolved, fromNamespace),
            $"ResolveType result should be reference-equal to programModel.Namespaces[\"{ns}\"].Types[\"{registeredName}\"].");
    }
}
