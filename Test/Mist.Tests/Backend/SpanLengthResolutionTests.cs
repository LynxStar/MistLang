// Tier 3 (Component): a dependently-typed Standard type answers its length from the type parameter
// alone, built over the REAL Standard sources. No Roslyn and no process spawning.

using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.ProgramModel.Construction;
using Mist.Tests.Helpers;
using CompilationUnit = Mist.Driver.Contracts.CompilationUnit;

namespace Mist.Tests.Backend;

/// <summary>
/// The length of a span and of an array is its dependent TYPE PARAMETER, and nothing else on the type
/// may answer to that name. A second declaration -- a property Standard spells, or one a projection
/// adds -- makes every read of it MIST_PM_AMBIGUOUS_MEMBER, which is a whole feature area refused
/// rather than a member degraded. Built over the real Standard sources, so re-adding one fails here.
/// </summary>
public sealed class SpanLengthResolutionTests
{
    [Theory]
    [InlineData("Span<int> s = new Span<int>(numbers); int n = s.Length;")]
    [InlineData("ReadOnlySpan<int> s = new ReadOnlySpan<int>(numbers); int n = s.Length;")]
    [InlineData("int n = numbers.Length;")]
    public void LengthResolves(string statement)
    {
        Assert.DoesNotContain(
            DiagnosticsFor(statement),
            diagnostic => diagnostic.Id.Value == "MIST_PM_AMBIGUOUS_MEMBER");
    }

    [Theory]
    [InlineData("Span<int> s = new Span<int>(numbers); int n = s.Length;")]
    [InlineData("int n = numbers.Length;")]
    public void LengthIsTheTypeParameter(string statement)
    {
        Assert.DoesNotContain(
            DiagnosticsFor(statement),
            diagnostic => diagnostic.Id.Value == "MIST_PM_UNRESOLVED_MEMBER");
    }

    /// <summary>
    /// Standard's own sources are parsed alongside the probe rather than stubbed, because the claim is
    /// about what Standard DECLARES. The ambient provider adds nothing: a CLR projection would answer
    /// the question with members this test is not about.
    /// </summary>
    private static ImmutableArray<Diagnostic> DiagnosticsFor(string statement)
    {
        var source = $$"""
            using Standard;

            namespace Probe;

            public class Probe
            {
                public static void Run()
                {
                    int[] numbers = [1, 2, 3];
                    {{statement}}
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

        var outputDir = Path.Combine(Path.GetTempPath(), "mist-span-length-" + Guid.NewGuid().ToString("N"));

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

        _ = ProgramModelBuilder.Build(
            new MistCompilation(stdlibUnits.Add(new CompilationUnit(sourceText)))
            {
                HasStandardLibrary = true,
            },
            stdlibAsts.Add(parseResult.Root),
            options,
            buildDiagnostics,
            NullAmbientTypeProvider.Instance);

        return buildDiagnostics.ToImmutableArray();
    }
}