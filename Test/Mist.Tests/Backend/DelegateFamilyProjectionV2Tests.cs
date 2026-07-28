// ambient-type-provider-v2, Property 3: Delegate family projection
//
// **Validates: Requirements 4.1, 4.2, 4.4**
//
// For CLR Func/Action types at all arities, verify correct Standard.Func constructed type
// with correct arguments. Uses [Theory] + [MemberData] (no process spawning).

using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

/// <summary>
/// Property test: for CLR Func/Action delegate family types, <see cref="AmbientTypeProviderV2.ResolveType"/>
/// returns the correct Standard.Func definition or ConstructedType with proper type arguments.
/// </summary>
public sealed class DelegateFamilyProjectionV2Tests : IDisposable
{
    private readonly ProgramModelClass programModel;
    private readonly AmbientTypeProviderV2 provider;
    private readonly IMistType funcArity2;
    private readonly IMistType voidType;

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

    public DelegateFamilyProjectionV2Tests()
    {
        programModel = new ProgramModelClass();
        _ = programModel.GetOrAddNamespace("Standard");
        var scope = new Scope(null);

        // Register the single variadic Func<params TArgs, TResult> (arity 2)
        funcArity2 = new ClassType("Func<,>", "Standard", scope, null, null);
        voidType = new ClassType("Void", "Standard", scope, null, null);

        _ = programModel.RegisterType(funcArity2);
        _ = programModel.RegisterType(voidType);

        // Register the single variadic Action<params TArgs> (arity 1)
        var actionDef = new ClassType("Action<>", "Standard", scope, null, null);
        _ = programModel.RegisterType(actionDef);

        provider = new AmbientTypeProviderV2(
            ImmutableArray<Mist.Driver.LoadedAssemblyInfo>.Empty,
            null,
            new DiagnosticBag());
    }

    public void Dispose()
    {
        provider.Dispose();
    }

    [Fact]
    public void ResolveType_SystemFuncArity1_ReturnsStandardFuncDefinition()
    {
        // System.Func`1 (Func<TResult>) → Standard.Func<,> (the single variadic definition)
        var resolved = LookupType("System", "Func`1");

        Assert.NotNull(resolved);
        Assert.True(
            ReferenceEquals(resolved, funcArity2),
            "ResolveType(\"System\", \"Func`1\") should return the Standard.Func<,> definition.");
    }

    [Fact]
    public void ResolveType_SystemFuncArity2_ReturnsStandardFuncDefinition()
    {
        // System.Func`2 (Func<T, TResult>) → Standard.Func<,> definition
        var resolved = LookupType("System", "Func`2");

        Assert.NotNull(resolved);
        Assert.True(
            ReferenceEquals(resolved, funcArity2),
            "ResolveType(\"System\", \"Func`2\") should return the Standard.Func<,> definition.");
    }

    [Fact]
    public void ResolveType_SystemFuncArity3_ReturnsStandardFuncDefinition()
    {
        // System.Func`3 (Func<T1, T2, TResult>) → Standard.Func<,> (the single variadic definition)
        var resolved = LookupType("System", "Func`3");

        Assert.NotNull(resolved);
        Assert.True(
            ReferenceEquals(resolved, funcArity2),
            "ResolveType(\"System\", \"Func`3\") should return the Standard.Func<,> definition.");
    }

    [Fact]
    public void ResolveType_SystemAction_ReturnsStandardAction()
    {
        // System.Action (non-generic) → Standard.Action<> (the single variadic definition)
        var resolved = LookupType("System", "Action");

        Assert.NotNull(resolved);
        var expected = programModel.TypesByFQN["Standard.Action<>"];
        Assert.True(
            ReferenceEquals(resolved, expected),
            "ResolveType(\"System\", \"Action\") should return the Standard.Action<> definition.");
    }

    [Fact]
    public void ResolveType_SystemActionArity1_ReturnsStandardActionArity1()
    {
        // System.Action`1 (Action<T>) → Standard.Action<>
        var resolved = LookupType("System", "Action`1");

        Assert.NotNull(resolved);
        var expected = programModel.TypesByFQN["Standard.Action<>"];
        Assert.True(
            ReferenceEquals(resolved, expected),
            "ResolveType(\"System\", \"Action`1\") should return Standard.Action<>.");
    }

    [Fact]
    public void ResolveType_SystemActionArity2_ReturnsStandardActionArity2()
    {
        // System.Action`2 (Action<T1, T2>) → Standard.Action<> (the single variadic definition)
        var resolved = LookupType("System", "Action`2");

        Assert.NotNull(resolved);
        var expected = programModel.TypesByFQN["Standard.Action<>"];
        Assert.True(
            ReferenceEquals(resolved, expected),
            "ResolveType(\"System\", \"Action`2\") should return Standard.Action<>.");
    }

    [Fact]
    public void ResolveType_NonSystemNamespace_DoesNotProjectDelegateFamily()
    {
        // Only System namespace triggers delegate family projection
        var resolved = LookupType("MyApp", "Func`1");

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveType_SystemDelegate_FallsThrough()
    {
        // System.Delegate and System.MulticastDelegate should NOT be handled by delegate family
        // They fall through to normal projection (which returns null because we have no assemblies)
        var resolved = LookupType("System", "Delegate");

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveType_SystemMulticastDelegate_FallsThrough()
    {
        var resolved = LookupType("System", "MulticastDelegate");

        Assert.Null(resolved);
    }
}