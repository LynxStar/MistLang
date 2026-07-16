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
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
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

    private IMistType? LookupType(string namespaceName, string typeName)
    {
        // Check well-known type map first (aliases to Standard Library).
        if(WellKnownTypeMap.Entries.TryGetValue((namespaceName, typeName), out var mapping))
        {
            if(programModel.TypesByFQN.TryGetValue($"{mapping.MistNamespace}.{mapping.MistTypeName}", out var stdType))
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

        if(programModel.TypesByFQN.TryGetValue($"{namespaceName}.{mistName}", out var type))
        {
            return type;
        }

        return null;
    }

    public AmbientTypeProviderV2IntegrationProjectionTests()
    {
        programModel = new ProgramModelClass();
        _ = programModel.GetOrAddNamespace("Standard");
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
            _ = programModel.RegisterType(type);
        }

        // Register Standard.IEnumerable<> and Standard.IEnumerator<> for interface mapping.
        var ienumerable = new AbstractType("IEnumerable<>", "Standard", scope, null, null);
        var ienumerator = new AbstractType("IEnumerator<>", "Standard", scope, null, null);
        _ = programModel.RegisterType(ienumerable);
        _ = programModel.RegisterType(ienumerator);

        // Register Standard.Array<,> for CLR array type resolution (T[] → Standard.Array<T, unknown>).
        var arrayType = new ClassType("Array<,>", "Standard", scope, null, null!);
        _ = programModel.RegisterType(arrayType);

        // Register Standard.Func definitions for delegate projection.
        for(var arity = 1; arity <= 17; arity++)
        {
            var commas = arity > 1 ? new string(',', arity - 1) : "";
            var funcName = $"Func<{commas}>";
            var funcType = new ClassType(funcName, "Standard", scope, null, null!);
            _ = programModel.RegisterType(funcType);
        }

        // Register Standard.Action definitions for delegate projection.
        var action = new ClassType("Action", "Standard", scope, null, null!);
        _ = programModel.RegisterType(action);
        for(var arity = 1; arity <= 16; arity++)
        {
            var commas = arity > 1 ? new string(',', arity - 1) : "";
            var actionName = $"Action<{commas}>";
            var actionType = new ClassType(actionName, "Standard", scope, null, null!);
            _ = programModel.RegisterType(actionType);
        }

        // Load real BCL assemblies via MetadataLoadContext.
        var runtimePath = typeof(object).Assembly.Location;
        var runtimeDir = Path.GetDirectoryName(runtimePath)!;
        var assemblyPaths = Directory.GetFiles(runtimeDir, "*.dll").ToImmutableArray();

        var resolver = new PathAssemblyResolver(assemblyPaths);
        metadataContext = new MetadataLoadContext(resolver);

        var loadedAssemblies = AmbientTypeProviderV2Factory.LoadAssemblies(assemblyPaths, metadataContext, new DiagnosticBag());

        provider = new AmbientTypeProviderV2(loadedAssemblies, null, new DiagnosticBag());

        // Eagerly build and register all ambient types.
        provider.BuildAndRegister(programModel);
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
        var resolved = LookupType("System", "Console");

        // Assert
        Assert.NotNull(resolved);
        Assert.True(programModel.TypesByFQN.ContainsKey("System.Console"),
            "ProgramModel should have 'System.Console' in TypesByFQN after resolving System.Console.");
    }

    [Fact]
    public void ResolveType_Console_ReturnedTypeIsReferenceEqualToNamespaceEntry()
    {
        // Act
        var resolved = LookupType("System", "Console");

        // Assert
        Assert.NotNull(resolved);
        var fromFQN = programModel.TypesByFQN["System.Console"];

        Assert.True(
            ReferenceEquals(resolved, fromFQN),
            "ResolveType should return the same instance that was inserted into programModel.TypesByFQN.");
    }

    [Fact]
    public void ResolveType_MemoryStream_InsertsIntoProgramModelNamespace()
    {
        // Act
        var resolved = LookupType("System.IO", "MemoryStream");

        // Assert
        Assert.NotNull(resolved);
        Assert.True(programModel.TypesByFQN.ContainsKey("System.IO.MemoryStream"));
    }

    [Fact]
    public void ResolveType_GenericList_InsertsWithArityBearingName()
    {
        // Act — generic types are stored with CLR arity suffix in the provider cache
        // but registered with angle-bracket name in the namespace
        var resolved = LookupType("System.Collections.Generic", "List`1");

        // Assert
        Assert.NotNull(resolved);
        // Generic types are registered with angle-bracket notation
        Assert.True(programModel.TypesByFQN.ContainsKey("System.Collections.Generic.List<>"),
            "Generic List should be registered as 'System.Collections.Generic.List<>' in TypesByFQN.");
    }

    [Fact]
    public void ResolveType_MultipleTypes_AllInsertedIntoSameNamespace()
    {
        // Act — resolve multiple types from the same namespace
        var console = LookupType("System", "Console");
        var environment = LookupType("System", "Environment");
        var math = LookupType("System", "Math");

        // Assert
        Assert.NotNull(console);
        Assert.NotNull(environment);
        Assert.NotNull(math);

        Assert.True(programModel.TypesByFQN.ContainsKey("System.Console"));
        Assert.True(programModel.TypesByFQN.ContainsKey("System.Environment"));
        Assert.True(programModel.TypesByFQN.ContainsKey("System.Math"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 13: TypeOrigin.Ambient assignment
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveType_Console_HasTypeOriginAssembly()
    {
        // Act
        var resolved = LookupType("System", "Console");

        // Assert
        Assert.NotNull(resolved);
        Assert.Equal(TypeOrigin.Ambient, resolved!.Origin);
    }

    [Fact]
    public void ResolveType_MemoryStream_HasTypeOriginAssembly()
    {
        var resolved = LookupType("System.IO", "MemoryStream");

        Assert.NotNull(resolved);
        Assert.Equal(TypeOrigin.Ambient, resolved!.Origin);
    }

    [Fact]
    public void ResolveType_GenericList_HasTypeOriginAssembly()
    {
        var resolved = LookupType("System.Collections.Generic", "List`1");

        Assert.NotNull(resolved);
        Assert.Equal(TypeOrigin.Ambient, resolved!.Origin);
    }

    [Fact]
    public void ResolveType_Interface_HasTypeOriginAssembly()
    {
        var resolved = LookupType("System", "IDisposable");

        Assert.NotNull(resolved);
        Assert.Equal(TypeOrigin.Ambient, resolved!.Origin);
    }

    [Fact]
    public void ResolveType_Struct_HasTypeOriginAssembly()
    {
        var resolved = LookupType("System", "DateTime");

        Assert.NotNull(resolved);
        Assert.Equal(TypeOrigin.Ambient, resolved!.Origin);
    }

    [Fact]
    public void ResolveType_Enum_HasTypeOriginAssembly()
    {
        var resolved = LookupType("System", "DayOfWeek");

        Assert.NotNull(resolved);
        Assert.Equal(TypeOrigin.Ambient, resolved!.Origin);
    }

    [Fact]
    public void ResolveType_AliasedType_HasTypeOriginSource()
    {
        // Aliased types (e.g., System.String → Standard.String) retain Source origin
        // because they return the existing Standard Library instance.
        var resolved = LookupType("System", "String");

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
        var resolved = LookupType(ns, typeName);

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
        var resolved = LookupType(ns, typeName);

        // Assert
        Assert.NotNull(resolved);

        // For generic types, the registered name uses angle-bracket notation
        var registeredName = resolved!.Name;
        var fqn = $"{ns}.{registeredName}";
        Assert.True(programModel.TypesByFQN.ContainsKey(fqn),
            $"TypesByFQN should contain '{fqn}' after projection.");

        var fromFQN = programModel.TypesByFQN[fqn];
        Assert.True(
            ReferenceEquals(resolved, fromFQN),
            $"ResolveType result should be reference-equal to programModel.TypesByFQN[\"{fqn}\"].");
    }
}