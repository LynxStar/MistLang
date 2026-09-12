// Tier 3 (Component): the one table from an intrinsic Standard type to the CLR name the emitter writes
// for it, held against the real Standard sources so a new intrinsic type without a spelling fails here
// rather than as a Roslyn error against generated code. The emitted text is asserted because a bare name
// and a full one print the same. No Roslyn compile and no process spawning.

using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class ClrSpellingTests : IDisposable
{
    private readonly string outputDir =
        Path.Combine(Path.GetTempPath(), "mist-clr-spelling-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Console lives in System.Console.dll, which a bare Type.GetType never searches, so the oracle
    /// reads every assembly the test process has loaded; naming the type is what loads it.
    /// </summary>
    private static readonly Func<string, Type?> ClrType = name =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name))
            .FirstOrDefault(type => type is not null)
        ?? (typeof(Console).Assembly.GetType(name));

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public void EveryTopLevelIntrinsicTypeStandardDeclaresIsListed()
    {
        var model = StandardModelProbe.Build("", out _);
        var unlisted = model.StandardAssembly!.CompilationUnits
            .SelectMany(unit => unit.Types)
            .OfType<ConcreteType>()
            .Where(type => type.IsIntrinsic && !ClrSpelling.IsListed(type))
            .Select(type => type.FullyQualifiedName)
            .ToList();

        Assert.True(unlisted.Count == 0, "Intrinsic types with no CLR spelling:\n" + string.Join("\n", unlisted));
    }

    [Fact]
    public void EveryListedNameIsAStandardDeclaration()
    {
        var model = StandardModelProbe.Build("", out _);
        var unknown = ClrSpelling.MistNames.Where(name => !model.TypesByFQN.ContainsKey(name)).ToList();

        Assert.True(unknown.Count == 0, "Listed names Standard does not declare:\n" + string.Join("\n", unknown));
    }

    [Fact]
    public void AnIntrinsicTypeIsWrittenInFull()
    {
        var emitted = EmitBody("Console.WriteLine(Math.Abs(-1));");

        Assert.Contains("System.Console.WriteLine(System.Math.Abs(-1))", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void AKeywordAliasIsWrittenAsTheKeyword()
    {
        var emitted = EmitBody("Int32 n = 1;\nString s = \"a\";");

        Assert.Contains("int n = 1;", emitted, StringComparison.Ordinal);
        Assert.Contains("string s = \"a\";", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEmittedFileCarriesOnlyTheSourcesUsings()
    {
        var model = StandardModelProbe.Build("Console.WriteLine(1);", out var built);
        AssertClean(built);

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create("Probe.mist"),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "Probe",
            NamespaceRoot: "Probe",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            OutputType: ProjectOutputType.Library);

        _ = new CSharpEmitterV2(model, options, ImmutableArray<string>.Empty, new DiagnosticBag(), ClrType).Emit();

        var probe = File.ReadAllText(Directory.EnumerateFiles(outputDir, "Probe.cs", SearchOption.AllDirectories).Single());
        var usings = probe.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("using ", StringComparison.Ordinal)).ToList();

        Assert.Equal(["using Standard;"], usings);
        Assert.Contains("System.Console.WriteLine(1);", probe, StringComparison.Ordinal);
    }

    private static void AssertClean(ImmutableArray<Diagnostic> diagnostics)
    {
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}").ToList();

        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    private static string EmitBody(string statements)
    {
        var model = StandardModelProbe.Build(statements, out var built);
        AssertClean(built);

        var run = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(m => m.Name == "Run");
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        Assert.NotNull(run.Body);

        using(IntrinsicSupply.Enter(model, ClrType))
        using(AsyncPromotion.Enter(model))
        {
            BodyEmitter.EmitMethodBody(run, run.Body, writer, diagnostics);
        }

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));
        return writer.ToString();
    }
}