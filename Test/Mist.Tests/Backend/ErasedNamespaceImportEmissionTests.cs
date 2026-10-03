using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "4")]
public sealed class ErasedNamespaceImportEmissionTests : ProgramModelProbe
{
    private readonly string emitDir = Path.Combine(Path.GetTempPath(), "mist-erased-import-emission-" + Guid.NewGuid().ToString("N"));

    public ErasedNamespaceImportEmissionTests()
        : base("MistErasedImportEmission_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    [Fact]
    public void AnImportedAliasBindsAndTheEmittedFileDoesNotImportItsNamespace()
    {
        var model = Build(
            """
            using Aliases;

            namespace Probe;

            Count answer = 42;
            """,
            referenced: "namespace Aliases; public type Count = int;",
            out var built);
        Assert.Empty(built.Where(d => d.Severity == DiagnosticSeverity.Error));

        var options = new CompilerOptions(
            ProjectFolder: emitDir,
            SourcePaths: ImmutableArray.Create("Program.mist"),
            Backend: new BackendId("csharp"),
            OutputDirectory: emitDir,
            BinDirectory: Path.Combine(emitDir, "bin"),
            ProjectName: "Probe",
            NamespaceRoot: "Probe");

        Assert.NotNull(new CSharpEmitterV2(model, options, ImmutableArray<string>.Empty, new DiagnosticBag(), _ => null).Transpile());

        var emitted = File.ReadAllText(Directory.EnumerateFiles(emitDir, "Program.cs", SearchOption.AllDirectories).Single());
        Assert.DoesNotContain("using Aliases;", emitted, StringComparison.Ordinal);
        Assert.Contains("int answer = 42;", emitted, StringComparison.Ordinal);
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing && Directory.Exists(emitDir))
        {
            Directory.Delete(emitDir, recursive: true);
        }

        base.Dispose(disposing);
    }
}