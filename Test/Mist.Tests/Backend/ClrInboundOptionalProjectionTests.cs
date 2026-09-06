using System.Collections.Immutable;
using System.Reflection;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

/// <summary>
/// The v2 half of the CLR null boundary: a reference-typed result the CLR hands back projects as
/// <c>T | void</c>, and a value-typed one does not.
/// <para>
/// ASSERTED ON THE PROJECTED SIGNATURE rather than on a diagnostic, because v2 runs no analyzers.
/// The refusal a user sees for a plain dot on one of these is an ordinary member-lookup miss, which
/// would read the same if the union had been built for an unrelated reason -- only the projected
/// type says the wrap happened and says which constituent it added.
/// </para>
/// </summary>
public sealed class ClrInboundOptionalProjectionTests : IDisposable
{
    private readonly ProgramModelClass programModel;
    private readonly AmbientTypeProviderV2 provider;
    private readonly MetadataLoadContext metadataContext;
    private readonly ConcreteType stringType;

    public ClrInboundOptionalProjectionTests()
    {
        programModel = new ProgramModelClass();
        AmbientProviderTestHelper.RegisterStandardTypes(programModel);

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var assemblyPaths = Directory.GetFiles(runtimeDir, "*.dll").ToImmutableArray();

        metadataContext = new MetadataLoadContext(new PathAssemblyResolver(assemblyPaths));

        var loaded = AmbientTypeProviderV2Factory.LoadAssemblies(
            assemblyPaths, metadataContext, new DiagnosticBag());

        provider = new AmbientTypeProviderV2(loaded, null, new DiagnosticBag());

        stringType = (ConcreteType)programModel.TypesByFQN["Standard.String"];

        provider.BuildAndRegister(programModel);
        provider.BridgeWellKnownMembers(programModel);
    }

    public void Dispose()
    {
        provider.Dispose();
        metadataContext.Dispose();
    }

    private IMistType ReturnTypeOf(string name, int arity)
    {
        var method = stringType.MethodsList
            .First(candidate => candidate.Name == name && candidate.ParametersList.Count == arity);

        return method.ReturnType;
    }

    [Fact]
    public void AClrReferenceReturn_ProjectsAsAUnionWithVoid()
    {
        var union = Assert.IsAssignableFrom<IUnionType>(ReturnTypeOf("ToUpper", 0));

        Assert.Contains(union.Constituents, c => c.FullyQualifiedName == "Standard.Void");
        Assert.Contains(union.Constituents, c => c.FullyQualifiedName == "Standard.String");
    }

    [Fact]
    public void AClrValueTypeReturn_IsNotWrapped()
    {
        var returnType = ReturnTypeOf("IndexOf", 1);

        Assert.False(
            returnType is IUnionType,
            $"expected a plain value type, projected as '{returnType.FullyQualifiedName}'");
    }

    /// <summary>
    /// The outbound half. Concat takes reference-typed arguments; Mist has no null to hand them, so
    /// a union there would only give overload resolution more candidates to be ambiguous between.
    /// </summary>
    [Fact]
    public void AClrReferenceParameter_IsNotWrapped()
    {
        var concat = stringType.MethodsList
            .First(candidate => candidate.Name == "Concat" && candidate.ParametersList.Count == 2);

        Assert.All(
            concat.ParametersList,
            parameter => Assert.False(
                parameter.Type is IUnionType,
                $"parameter '{parameter.Name}' projected as '{parameter.Type.FullyQualifiedName}'"));
    }
}