using System.Collections.Immutable;
using System.Reflection;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

/// <summary>
/// The v2 half of the CLR null boundary: a reference-typed result the CLR says may be null projects
/// as <c>T | void</c>, and one it declares non-null does not.
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
    private readonly MemberType pathType;

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

        provider.BuildAndRegister(programModel);

        pathType = (MemberType)programModel.TypesByFQN["System.IO.Path"];
    }

    public void Dispose()
    {
        provider.Dispose();
        metadataContext.Dispose();
    }

    /// <summary>
    /// The string overload, System.IO.Path declaring a span twin of every member used here.
    /// </summary>
    private Method StringOverload(string name, int arity)
    {
        return pathType.MethodsList.Single(candidate =>
            candidate.Name == name
            && candidate.ParametersList.Count == arity
            && candidate.ParametersList.All(parameter => !parameter.Type.FullyQualifiedName.Contains("Span")));
    }

    /// <summary>
    /// <c>Path.GetDirectoryName(string?)</c> is annotated <c>string?</c>, which is the only reason
    /// this one wraps -- <c>Path.Combine</c> declares its result non-null.
    /// </summary>
    [Fact]
    public void ANullableClrReferenceReturn_ProjectsAsAUnionWithVoid()
    {
        var union = Assert.IsAssignableFrom<IUnionType>(StringOverload("GetDirectoryName", 1).ReturnType);

        Assert.Contains(union.Constituents, c => c.FullyQualifiedName == "Standard.Void");
        Assert.Contains(union.Constituents, c => c.FullyQualifiedName == "Standard.String");
    }

    /// <summary>
    /// The other direction, and the one that makes the rule worth having. Wrapping a result the CLR
    /// PROMISES is never null makes a caller narrow something that cannot be absent -- which is what
    /// stopped a chained `xs.Skip(5).First()` resolving at all.
    /// </summary>
    [Fact]
    public void ANonNullClrReferenceReturn_IsNotWrapped()
    {
        var returnType = StringOverload("Combine", 2).ReturnType;

        Assert.False(
            returnType is IUnionType,
            $"expected a plain reference type, projected as '{returnType.FullyQualifiedName}'");
    }

    [Fact]
    public void AClrValueTypeReturn_IsNotWrapped()
    {
        var returnType = StringOverload("HasExtension", 1).ReturnType;

        Assert.False(
            returnType is IUnionType,
            $"expected a plain value type, projected as '{returnType.FullyQualifiedName}'");
    }

    /// <summary>
    /// The invariant the generic half of this rests on. A result whose type is a type PARAMETER
    /// cannot be wrapped here -- List's element would take the wrap that List of Int32 must not
    /// have -- so the decision moves to substitution, and this flag is the only thing carrying
    /// "the CLR could have put a null here" to it.
    /// </summary>
    [Fact]
    public void AProjectedMember_IsMarkedAsClrProjected()
    {
        Assert.True(StringOverload("Combine", 2).Modifiers.IsClrProjected);
    }

    /// <summary>
    /// The outbound half. GetDirectoryName takes a <c>string?</c>; Mist has no null to hand it, so
    /// a union there would only give overload resolution more candidates to be ambiguous between.
    /// </summary>
    [Fact]
    public void AClrReferenceParameter_IsNotWrapped()
    {
        var combine = StringOverload("GetDirectoryName", 1);

        Assert.All(
            combine.ParametersList,
            parameter => Assert.False(
                parameter.Type is IUnionType,
                $"parameter '{parameter.Name}' projected as '{parameter.Type.FullyQualifiedName}'"));
    }
}