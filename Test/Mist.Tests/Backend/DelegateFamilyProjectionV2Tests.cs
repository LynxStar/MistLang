// ambient-type-provider-v2, Property 3: Delegate family projection
//
// **Validates: Requirements 4.1, 4.2, 4.4**
//
// For CLR Func/Action types at all arities, verify correct Standard.Func constructed type
// with correct arguments. Uses [Theory] + [MemberData] (no process spawning).

using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
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
    private readonly IMistType funcArity1;
    private readonly IMistType funcArity2;
    private readonly IMistType funcArity3;
    private readonly IMistType voidType;

    public DelegateFamilyProjectionV2Tests()
    {
        programModel = new ProgramModelClass();
        var stdNs = programModel.GetOrAddNamespace("Standard");
        var scope = new Scope(null);

        // Register Standard.Func definitions at arities 1-3
        funcArity1 = new ClassType("Func<>", "Standard", scope, null, null);
        funcArity2 = new ClassType("Func<,>", "Standard", scope, null, null);
        funcArity3 = new ClassType("Func<,,>", "Standard", scope, null, null);
        voidType = new ClassType("Void", "Standard", scope, null, null);

        stdNs.Types["Func<>"] = funcArity1;
        stdNs.Types["Func<,>"] = funcArity2;
        stdNs.Types["Func<,,>"] = funcArity3;
        stdNs.Types["Void"] = voidType;

        provider = new AmbientTypeProviderV2(
            ImmutableArray<Mist.Driver.LoadedAssemblyInfo>.Empty,
            programModel.TypeInterner,
            null);
    }

    public void Dispose()
    {
        provider.Dispose();
    }

    [Fact]
    public void ResolveType_SystemFuncArity1_ReturnsStandardFuncDefinition()
    {
        // System.Func`1 (Func<TResult>) → Standard.Func<> definition
        var resolved = provider.ResolveType("System", "Func`1", programModel);

        Assert.NotNull(resolved);
        Assert.True(
            ReferenceEquals(resolved, funcArity1),
            "ResolveType(\"System\", \"Func`1\") should return the Standard.Func<> definition.");
    }

    [Fact]
    public void ResolveType_SystemFuncArity2_ReturnsStandardFuncDefinition()
    {
        // System.Func`2 (Func<T, TResult>) → Standard.Func<,> definition
        var resolved = provider.ResolveType("System", "Func`2", programModel);

        Assert.NotNull(resolved);
        Assert.True(
            ReferenceEquals(resolved, funcArity2),
            "ResolveType(\"System\", \"Func`2\") should return the Standard.Func<,> definition.");
    }

    [Fact]
    public void ResolveType_SystemFuncArity3_ReturnsStandardFuncDefinition()
    {
        // System.Func`3 (Func<T1, T2, TResult>) → Standard.Func<,,> definition
        var resolved = provider.ResolveType("System", "Func`3", programModel);

        Assert.NotNull(resolved);
        Assert.True(
            ReferenceEquals(resolved, funcArity3),
            "ResolveType(\"System\", \"Func`3\") should return the Standard.Func<,,> definition.");
    }

    [Fact]
    public void ResolveType_SystemAction_ReturnsConstructedTypeWithVoidArg()
    {
        // System.Action (non-generic) → ConstructedType(Standard.Func<>, [Standard.Void])
        var resolved = provider.ResolveType("System", "Action", programModel);

        Assert.NotNull(resolved);
        var constructed = Assert.IsAssignableFrom<IConstructedType>(resolved!);
        Assert.True(
            ReferenceEquals(constructed.Definition, funcArity1),
            "Action (non-generic) should use Standard.Func<> as the definition.");
        _ = Assert.Single(constructed.TypeArguments);
        Assert.True(
            ReferenceEquals(constructed.TypeArguments[0], voidType),
            "Action (non-generic) should have Standard.Void as the sole type argument.");
    }

    [Fact]
    public void ResolveType_SystemActionArity1_ReturnsStandardFuncArity2Definition()
    {
        // System.Action`1 (Action<T>) → Standard.Func<,> definition (arity+1)
        var resolved = provider.ResolveType("System", "Action`1", programModel);

        Assert.NotNull(resolved);
        Assert.True(
            ReferenceEquals(resolved, funcArity2),
            "ResolveType(\"System\", \"Action`1\") should return the Standard.Func<,> definition (arity N+1 = 2).");
    }

    [Fact]
    public void ResolveType_SystemActionArity2_ReturnsStandardFuncArity3Definition()
    {
        // System.Action`2 (Action<T1, T2>) → Standard.Func<,,> definition (arity+1)
        var resolved = provider.ResolveType("System", "Action`2", programModel);

        Assert.NotNull(resolved);
        Assert.True(
            ReferenceEquals(resolved, funcArity3),
            "ResolveType(\"System\", \"Action`2\") should return the Standard.Func<,,> definition (arity N+1 = 3).");
    }

    [Fact]
    public void ResolveType_NonSystemNamespace_DoesNotProjectDelegateFamily()
    {
        // Only System namespace triggers delegate family projection
        var resolved = provider.ResolveType("MyApp", "Func`1", programModel);

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveType_SystemDelegate_FallsThrough()
    {
        // System.Delegate and System.MulticastDelegate should NOT be handled by delegate family
        // They fall through to normal projection (which returns null because we have no assemblies)
        var resolved = provider.ResolveType("System", "Delegate", programModel);

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveType_SystemMulticastDelegate_FallsThrough()
    {
        var resolved = provider.ResolveType("System", "MulticastDelegate", programModel);

        Assert.Null(resolved);
    }
}