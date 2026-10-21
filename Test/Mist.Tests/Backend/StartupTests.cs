using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Driver.Contracts;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class StartupTests
{
    [Fact]
    public void AnExecutableWithoutAnEntryPointCannotReachEmission()
    {
        var hand = new HandBuiltModel();
        _ = hand.Model.AddSelfAssembly("Probe");

        Assert.Null(Startup.Of(hand.Model, Options(ProjectOutputType.Executable)));
    }

    [Fact]
    public void ARefusedSelectionCannotReachEmission()
    {
        var hand = new HandBuiltModel();
        var assembly = hand.Model.AddSelfAssembly("Probe");
        foreach(var name in new[] { "A", "B" })
        {
            assembly.EntryPoints.Add(new Method("Main", hand.Type("Void"), [], new MemberModifiers { IsStatic = true })
            {
                DeclaringType = HandBuiltModel.Class(name),
            });
        }

        Assert.Null(Startup.Of(hand.Model, Options(ProjectOutputType.Executable)));
    }

    [Fact]
    public void ALibraryWithoutAnEntryPointCanReachEmission()
    {
        var hand = new HandBuiltModel();
        _ = hand.Model.AddSelfAssembly("Probe", isLibrary: true);

        Assert.Equal(new Startup(null, null), Startup.Of(hand.Model, Options(ProjectOutputType.Library)));
    }

    private static CompilerOptions Options(ProjectOutputType outputType)
    {
        return new CompilerOptions(
            ProjectFolder: "/project",
            SourcePaths: [],
            Backend: new BackendId("csharp"),
            OutputDirectory: "/project/out",
            BinDirectory: "/project/bin",
            ProjectName: "Probe",
            NamespaceRoot: "Probe",
            OutputType: outputType);
    }
}