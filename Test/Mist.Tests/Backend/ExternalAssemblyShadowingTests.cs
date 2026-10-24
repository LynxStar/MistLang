using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Driver;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

/// <summary>
/// A C# assembly compiled against a composed Mist library, beside that library's stale standalone copy,
/// as a referenced C# project's output directory holds them. Both are written with
/// <see cref="SyntheticAssembly"/>: <c>Lib.dll</c> defines <c>Lib.Thing</c>, and <c>Bridge.dll</c>'s
/// <c>Bridge.Runner</c> takes and returns one.
/// </summary>
[Trait("Tier", "2")]
public sealed class ExternalAssemblyShadowingTests : IDisposable
{
    private static readonly string RuntimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

    private readonly string outputDirectory = Path.Combine(Path.GetTempPath(), "mist-shadowing-" + Guid.NewGuid().ToString("N"));
    private readonly string library;
    private readonly string bridge;

    public ExternalAssemblyShadowingTests()
    {
        library = SyntheticAssembly.Write(outputDirectory, "Lib", "Lib", "Thing");
        bridge = SyntheticAssembly.Write(outputDirectory, "Bridge", "Bridge", "Runner", ("Lib", "Lib", "Thing"));
    }

    public void Dispose()
    {
        Directory.Delete(outputDirectory, recursive: true);
    }

    [Fact]
    public void AnAssemblyNamedLikeAComposedProject_IsNeverRegistered_AndItsTypesMeanTheDeclaredOnes()
    {
        var model = ModelDeclaringThing(out var declared);

        using var provider = AmbientTypeProviderV2Factory.CreateOver(
            [bridge, library], [RuntimeDirectory, outputDirectory], ["Lib"], new DiagnosticBag());
        provider.BuildAndRegister(model);

        Assert.Equal(["Bridge"], provider.LoadedAssemblies.Select(assembly => assembly.MetadataAssembly.GetName().Name));
        Assert.Null(provider.FindClrType("Lib.Thing"));
        Assert.Same(declared, model.TypesByFQN["Lib.Thing"]);
        AssertRunnerSpeaksOf(model, declared);
    }

    [Fact]
    public void AClosureMembersTypes_ResolveDuringReading_AndMeanTheDeclaredOnes()
    {
        var model = ModelDeclaringThing(out var declared);

        using var provider = AmbientTypeProviderV2Factory.CreateOver(
            [bridge], [RuntimeDirectory, outputDirectory], ["App"], new DiagnosticBag());
        provider.BuildAndRegister(model);

        Assert.Same(declared, model.TypesByFQN["Lib.Thing"]);
        AssertRunnerSpeaksOf(model, declared);
    }

    [Fact]
    public void AClosureMember_IsNotRegistered()
    {
        var model = new ProgramModelClass();
        AmbientProviderTestHelper.RegisterStandardTypes(model);

        using var provider = AmbientTypeProviderV2Factory.CreateOver(
            [bridge], [RuntimeDirectory, outputDirectory], [], new DiagnosticBag());
        provider.BuildAndRegister(model);

        _ = Assert.IsType<ClassType>(model.TypesByFQN["Bridge.Runner"]);
        Assert.False(model.TypesByFQN.ContainsKey("Lib.Thing"));
    }

    private static ProgramModelClass ModelDeclaringThing(out ClassType declared)
    {
        var model = new ProgramModelClass();
        AmbientProviderTestHelper.RegisterStandardTypes(model);

        declared = new ClassType("Thing", "Lib", new Scope(null), null, null!)
        {
            Assembly = model.AddReferenceAssembly("Lib", "/work/Lib/mist.toml"),
        };
        _ = model.GetOrAddNamespace("Lib");
        Assert.True(model.RegisterType(declared));
        return model;
    }

    private static void AssertRunnerSpeaksOf(ProgramModelClass model, ClassType declared)
    {
        var runner = Assert.IsType<ClassType>(model.TypesByFQN["Bridge.Runner"]);

        Assert.Same(declared, runner.MethodsList.Single(method => method.Name == "Use").ParametersList.Single().Type);
        var made = Assert.IsType<UnionType>(runner.MethodsList.Single(method => method.Name == "Make").ReturnType);
        Assert.Contains(declared, made.Constituents);
    }
}