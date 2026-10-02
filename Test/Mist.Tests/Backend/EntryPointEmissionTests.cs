using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// What the emitter does with the entry point that was selected, read off the sources it writes and
/// the main type it would hand Roslyn, because a program that ran the wrong <c>Main</c> or dropped its
/// statements still compiles. Stops short of the C# compile: <see cref="CSharpEmitterV2.Transpile"/>.
/// </summary>
[Trait("Tier", "3")]
public sealed class EntryPointEmissionTests : ProgramModelProbe
{
    private const string TwoMains = """
        namespace Probe;

        public class A
        {
            public static void Main() { }
        }

        public class B
        {
            public static void Main() { }
        }
        """;

    private readonly string emitDir = Path.Combine(Path.GetTempPath(), "mist-entry-point-emission-" + Guid.NewGuid().ToString("N"));
    private readonly DiagnosticBag emitDiagnostics = new();

    public EntryPointEmissionTests()
        : base("MistEntryPointEmission_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    [Fact]
    public void TheClassTheManifestNames_IsTheMainTypeAmongSeveral()
    {
        var startup = Transpile(TwoMains, entryPoint: "Probe.B");

        Assert.NotNull(startup);
        Assert.Equal("Probe.B", startup.MainTypeName);
        Assert.Null(startup.TopLevelUnit);
        Assert.Contains("class A", Emitted());
    }

    [Fact]
    public void StatementsChosenOverAMain_AreEmittedBesideItAndNoMainTypeIsNamed()
    {
        var startup = Transpile(
            """
            namespace Probe;

            var answer = 42;

            public class A
            {
                public static void Main() { }
            }
            """,
            entryPoint: "top-level");

        Assert.NotNull(startup);
        Assert.Null(startup.MainTypeName);
        Assert.EndsWith("Program.mist", startup.TopLevelUnit?.SourcePath);
        Assert.Contains("answer = 42;", Emitted());
        Assert.Contains("public static void Main()", Emitted());
    }

    [Theory]
    [InlineData("namespace Probe; public class Program { public static void Main() { } }", "Probe.Program")]
    [InlineData("namespace Probe; var shown = 1;", null)]
    public void AReferencedProjectsStatements_AreNotEmittedIntoTheProgramThatComposesIt(string source, string? mainType)
    {
        var startup = Transpile(source, referenced: "namespace Library; var hidden = 7; public class Helper { }");

        Assert.NotNull(startup);
        Assert.Equal(mainType, startup.MainTypeName);
        Assert.Contains("class Helper", Emitted());
        Assert.DoesNotContain("hidden", Emitted());
    }

    [Fact]
    public void ALibrary_NamesNoMainTypeThoughItDeclaresAMain()
    {
        var startup = Transpile(
            "namespace Probe; public class A { public static void Main() { } }", outputType: ProjectOutputType.Library);

        Assert.NotNull(startup);
        Assert.Null(startup.MainTypeName);
        Assert.Empty(emitDiagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnExecutableThatStartsAtNothing_IsRefusedBeforeAnythingIsWritten()
    {
        var startup = Transpile("namespace Probe; public class A { }");

        Assert.Null(startup);
        Assert.Equal("MIST_MISSING_ENTRY_POINT", Assert.Single(emitDiagnostics.ToImmutableArray()).Id.Value);
        Assert.False(Directory.Exists(emitDir));
    }

    [Fact]
    public void ARefusedSelection_IsReportedRatherThanEmittedAround()
    {
        var startup = Transpile(TwoMains);

        Assert.Null(startup);
        var refusal = Assert.Single(emitDiagnostics.ToImmutableArray());
        Assert.Equal(("MIST_EP_0004", "Probe"), (refusal.Id.Value, refusal.CompilationUnitPath));
        Assert.False(Directory.Exists(emitDir));
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing && Directory.Exists(emitDir))
        {
            Directory.Delete(emitDir, recursive: true);
        }

        base.Dispose(disposing);
    }

    private Startup? Transpile(
        string source,
        string? referenced = null,
        string? entryPoint = null,
        ProjectOutputType outputType = ProjectOutputType.Executable)
    {
        var model = Build(source, referenced, out var built, entryPoint: entryPoint, outputType: outputType);
        Assert.Empty(built.Where(d => d.Severity == DiagnosticSeverity.Error));

        var options = new CompilerOptions(
            ProjectFolder: emitDir,
            SourcePaths: ImmutableArray.Create("Program.mist"),
            Backend: new BackendId("csharp"),
            OutputDirectory: emitDir,
            BinDirectory: Path.Combine(emitDir, "bin"),
            ProjectName: "Probe",
            NamespaceRoot: "Probe",
            EntryPoint: entryPoint,
            OutputType: outputType);

        return new CSharpEmitterV2(model, options, ImmutableArray<string>.Empty, emitDiagnostics, _ => null).Transpile();
    }

    private string Emitted()
    {
        return string.Join("\n", Directory.EnumerateFiles(emitDir, "*.cs", SearchOption.AllDirectories).Order().Select(File.ReadAllText));
    }
}