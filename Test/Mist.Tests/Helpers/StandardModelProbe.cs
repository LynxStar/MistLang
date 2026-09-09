// Tier 3 (Component) harness: builds a real v2 ProgramModel over the REAL Standard sources plus one
// probe file. Distinct from ProgramModelProbe, which registers Standard as shells: a claim about what
// Standard DECLARES -- its aliases, its dependent parameters, the members it deliberately omits --
// cannot be made against a shell that has none of them. No Roslyn and no process spawning.

using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.ProgramModel.Construction;
using CompilationUnit = Mist.Driver.Contracts.CompilationUnit;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Helpers;

internal static class StandardModelProbe
{
    /// <summary>
    /// Wraps <paramref name="statements"/> in a probe method, builds the model alongside Standard's own
    /// sources, and hands back both. The ambient provider adds nothing: a CLR projection would answer
    /// with members these tests are not about.
    /// </summary>
    public static ProgramModelClass Build(
        string statements, out ImmutableArray<Diagnostic> diagnostics, string extraTypes = "")
    {
        var source = $$"""
            using Standard;

            namespace Probe;

            {{extraTypes}}

            public class Probe
            {
                public static void Run()
                {
                    {{statements}}
                }
            }
            """;

        var (stdlibUnits, stdlibAsts) = StdlibTestHelper.LoadStdlib();

        var parseDiagnostics = new DiagnosticBag();
        var sourceText = new SourceText("Probe.mist", source);
        var tokens = new MistLexer().Lex(sourceText, parseDiagnostics);
        var parseResult = new MistParser().Parse(tokens, parseDiagnostics);

        Assert.NotNull(parseResult.Root);
        Assert.Empty(parseDiagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        var outputDir = Path.Combine(Path.GetTempPath(), "mist-standard-probe-" + Guid.NewGuid().ToString("N"));

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create("Probe.mist"),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "Probe",
            NamespaceRoot: "Probe",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var buildDiagnostics = new DiagnosticBag();

        var model = ProgramModelBuilder.Build(
            new MistCompilation(stdlibUnits.Add(new CompilationUnit(sourceText)))
            {
                HasStandardLibrary = true,
            },
            stdlibAsts.Add(parseResult.Root),
            options,
            buildDiagnostics,
            NullAmbientTypeProvider.Instance);

        diagnostics = buildDiagnostics.ToImmutableArray();
        return Assert.IsType<ProgramModelClass>(model);
    }
}