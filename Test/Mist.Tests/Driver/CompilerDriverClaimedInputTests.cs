using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Driver;

[Trait("Tier", "4")]
public sealed class CompilerDriverClaimedInputTests : IDisposable
{
    private readonly string scratch = Path.Combine(Path.GetTempPath(), "mist-claimed-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingProgressReporter reporter = new();
    private readonly AmbientSource ambient = new();
    private PipelineInput? modelInput;
    private bool emitted;

    public CompilerDriverClaimedInputTests()
    {
        _ = Directory.CreateDirectory(Path.Combine(scratch, "App"));
    }

    [Fact]
    public void DiscoveryToTranslationToBinding_PreservesProjectFactsAndInferredNamespaces()
    {
        Write("App/mist.toml", """
            name = "App"
            [references]
            Lib = { path = "../Lib" }
            [global_usings]
            namespaces = ["Standard"]
            """);
        Write("Lib/mist.toml", """
            name = "Lib"
            output_type = "library"
            [global_usings]
            namespaces = ["Standard"]
            """);
        Write("App/RootType.probe", "claimed root");
        Write("Lib/Parts/RefType.probe", "claimed reference");
        Write("App/Parts/RefType.probe", "claimed root with matching relative path");
        Write("Lib/Declared.mist", "public class Declared { }");
        Write("Lib/out/Ignored.probe", "ignored");
        Write("Lib/bin/Ignored.probe", "ignored");
        Write("App/Consumer.mist", """
            public class Consumer
            {
                public static RootType Make() { return new RootType(); }
                public static Lib.Parts.RefType Other() { return new Lib.Parts.RefType(); }
            }
            """);
        var translator = new Translator();

        var result = Compile([translator]);

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.True(emitted);
        Assert.True(ambient.Registered);
        Assert.Equal(1, ambient.Disposals);
        var context = Assert.Single(translator.Contexts);
        Assert.Equal(3, context.Inputs.Length);
        Assert.Same(ambient, context.AmbientTypes);
        _ = Assert.IsType<MistLexer>(context.Lexer);
        _ = Assert.IsType<MistParser>(context.Parser);
        Assert.Equal(new ProjectFacts("App", At("App"), context.Project.GlobalUsings), context.Project);
        Assert.Equal<string>(["Standard"], context.Project.GlobalUsings);
        Assert.Equal(2, context.ParsedUnits.Length);
        Assert.All(context.ParsedUnits, unit => Assert.NotNull(unit.Ast));
        var referenceInput = Assert.Single(context.Inputs, input => input.Project.Name == "Lib");
        Assert.Equal<string>(["Standard"], referenceInput.Project.GlobalUsings);
        Assert.Equal(At("Lib"), referenceInput.Project.ProjectFolder);
        Assert.NotNull(modelInput);
        var generated = modelInput.Compilation.Units.Where(unit => unit.SourcePath is not null).ToArray();
        Assert.Equal(3, generated.Length);
        var rootUnit = Assert.Single(generated, unit => unit.SourcePath == At("App", "RootType.probe"));
        Assert.Equal(At("App", "out", "App", "translated", "RootType.probe.mist"), rootUnit.Source.Path);
        var refUnit = Assert.Single(generated, unit => unit.OriginProjectPath is not null);
        Assert.Equal(At("Lib", "mist.toml"), refUnit.OriginProjectPath);
        Assert.Equal(At("App", "out", "Lib", "translated", "Parts", "RefType.probe.mist"), refUnit.Source.Path);
        Assert.Equal(At("Lib"), refUnit.ProjectFolder);
        Assert.All(generated, unit => Assert.Equal(unit.Source.Contents, File.ReadAllText(unit.Source.Path)));
        Assert.Equal(["3 claimed inputs", "3 translated units"], reporter.DetailsOf("3.5"));
        Assert.Equal(["start 1", "complete 1", "start 2", "complete 2", "start 3", "complete 3",
            "start 3.5", "complete 3.5", "start 4", "complete 4", "start 5", "complete 5", "start 6", "complete 6"], reporter.Sequence);
    }

    [Fact]
    public void ClaimedOnlyProject_ReachesTheModelWithoutAMistFile()
    {
        Write("App/Only.probe", "claimed");
        var translator = new Translator();

        var result = Compile([translator]);

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.Empty(Assert.Single(translator.Contexts).ParsedUnits);
        Assert.NotNull(modelInput);
        _ = Assert.Single(modelInput.Compilation.Units, unit => !unit.IsStdlib);
    }

    [Fact]
    public void TranslatorErrors_RunEveryTranslatorAndStopAtTheParseGate()
    {
        Write("App/Only.probe", "claimed");
        var first = new Translator(reportError: true);
        var second = new Translator(reportError: true);

        var result = Compile([first, second]);

        Assert.False(result.Succeeded);
        _ = Assert.Single(first.Contexts);
        _ = Assert.Single(second.Contexts);
        Assert.Equal(2, result.Diagnostics.Count(d => d.Number == DriverDiagnostics.FileError.Number));
        Assert.Null(modelInput);
        Assert.DoesNotContain("start 4", reporter.Sequence);
        Assert.Equal(1, ambient.Disposals);
    }

    [Fact]
    public void TranslatedParseError_StopsBeforeModelConstruction()
    {
        Write("App/Only.probe", "claimed");

        var result = Compile([new Translator(invalid: true)]);

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Null(modelInput);
        Assert.False(emitted);
        Assert.DoesNotContain("start 4", reporter.Sequence);
        Assert.Equal(1, ambient.Disposals);
    }

    private CompilerResult Compile(ImmutableArray<ISourceTranslator> translators)
    {
        IBackend backend = new Backend(translators);
        var pipeline = new StandardPipeline(reporter, input =>
        {
            modelInput = input;
            Assert.Same(ambient, input.AmbientTypes);
            Assert.Equal(0, ambient.Disposals);
            return StandardPipeline.Build(input, _ => throw new InvalidOperationException("Stage 4 must reuse the driver's ambient source."));
        }, (_, _, _) => { }, (_, _, input) =>
        {
            emitted = true;
            return new Emission(new CompilerResult(true, null, input.Diagnostics.ToImmutableArray()), 0);
        });
        var driver = new CompilerDriver(new MistLexer(), new MistParser(), StdlibParseCache.Shared, reporter,
            ambientTypes: _ => ambient, backendRegistry: new BackendRegistry([backend]), pipeline: pipeline);
        var root = At("App");
        var sources = SourcePathSelection.ForReferencedProject(root, SourcePathSelection.EnumerateCandidates(root, backend.ClaimedExtensions));
        return driver.Compile(new CompilerOptions(root, sources, backend.Id, At("App", "out"), At("App", "bin"),
            "App", "App", OutputType: ProjectOutputType.Library));
    }

    private string At(params string[] parts)
    {
        return Path.Combine([scratch, .. parts]);
    }

    private void Write(string path, string text)
    {
        var fullPath = At(path);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, text);
    }

    public void Dispose()
    {
        Directory.Delete(scratch, recursive: true);
    }

    private sealed class Backend(ImmutableArray<ISourceTranslator> translators) : IBackend
    {
        public BackendId Id => new("probe");
        public ImmutableArray<ISourceTranslator> SourceTranslators => translators;
    }

    private sealed class Translator(bool reportError = false, bool invalid = false) : ISourceTranslator
    {
        public ImmutableArray<string> Extensions => [".probe"];
        public List<TranslationContext> Contexts { get; } = [];

        public ImmutableArray<TranslatedUnit> Translate(TranslationContext context)
        {
            Contexts.Add(context);
            if(reportError)
            {
                context.Diagnostics.ReportTyped(DriverDiagnostics.FileError, new FileOnly("probe"), new PathValue("probe"), new TextValue("translation failed"));
                return [];
            }

            return [.. context.Inputs.Select(input => new TranslatedUnit(new SourceText(input.Text.Path,
                invalid ? "public class {" : $"public class {Path.GetFileNameWithoutExtension(input.Text.Path)} {{ }}"), input.Project))];
        }
    }

    private sealed class AmbientSource : IAmbientTypeSource
    {
        public bool Registered { get; private set; }
        public int Disposals { get; private set; }

        public void BuildAndRegister(ProgramModelClass programModel)
        {
            Registered = true;
        }

        public Type? FindClrType(string fullName)
        {
            return StubAmbientTypeSource.Instance.FindClrType(fullName);
        }

        public void Dispose()
        {
            Disposals++;
        }
    }
}