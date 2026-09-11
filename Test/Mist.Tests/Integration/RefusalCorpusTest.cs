// Tier 5 (System): the refusals half of the ledger. One test over every program in refusals/, each
// compiled in-process on the experimental pipeline and held to the markers it carries.
//
// THE ANALOGUE OF A SAMPLE'S STDOUT, and the reason it has to exist at all: every sample is a VALID
// program, so an analyzer that reports nothing passes all of them. A refusal is observable nowhere
// else. See refusals/README.md for the marker convention and backlog/analysis-v2-capability-progression.yaml
// order 1 for why the corpus and the Tier 3 probe classes are both kept.
//
// One test rather than one per program, for the reason the baseline walker is one test: the corpus is
// the unit, and a case that stops being checked has to take the whole test red with it.

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Integration;

public sealed class RefusalCorpusTest : IDisposable
{
    private const string LineMarker = "REFUSE";
    private const string FileMarker = "REFUSE-FILE";

    private readonly string scratchDir;

    public RefusalCorpusTest()
    {
        scratchDir = Path.Combine(Path.GetTempPath(), "mist-refusals-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch { /* a leaked temp dir is not worth failing over */ }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void EveryRefusalCaseIsRefusedForTheStatedReason()
    {
        var corpusRoot = Path.Combine(FindRepoRoot(), "refusals");
        var cases = Directory.GetFiles(corpusRoot, "*.mist", SearchOption.AllDirectories).OrderBy(p => p).ToList();

        Assert.True(cases.Count > 0, $"No refusal cases found under {corpusRoot}.");

        var failures = new List<string>();

        foreach(var path in cases)
        {
            var name = Path.GetRelativePath(corpusRoot, path);
            var expectations = Expectations(File.ReadAllLines(path));

            if(expectations.Count == 0)
            {
                failures.Add($"{name}: carries no {LineMarker} marker, so it does not say what it is for.");
                continue;
            }

            failures.AddRange(Check(name, expectations, Compile(path)));
        }

        Assert.True(
            failures.Count == 0,
            $"{failures.Count} refusal case(s) failed:\n  " + string.Join("\n  ", failures));
    }

    /// <summary>
    /// A marker predicts a diagnostic on the line BELOW it, or anywhere in the file for the
    /// file-scoped form. Line rather than line-and-column because a column moves with
    /// reformatting while the claim -- this line is refused, for this reason -- does not.
    /// </summary>
    private static List<Expectation> Expectations(string[] lines)
    {
        var found = new List<Expectation>();

        for(var i = 0; i < lines.Length; i++)
        {
            var match = Regex.Match(lines[i].Trim(), $@"^//\s*({FileMarker}|{LineMarker}):\s*(\S+)\s*$");

            if(!match.Success)
            {
                continue;
            }

            found.Add(new Expectation(
                match.Groups[2].Value,
                match.Groups[1].Value == FileMarker ? null : i + 2));
        }

        return found;
    }

    private static IEnumerable<string> Check(
        string name, List<Expectation> expectations, ImmutableArray<Diagnostic> diagnostics)
    {
        var errors = diagnostics
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .ToList();
        var claimed = new HashSet<Diagnostic>();

        foreach(var expectation in expectations)
        {
            var match = errors.FirstOrDefault(
                d => d.Id.Value == expectation.Id
                    && (expectation.Line is null || d.Location.Start.Line == expectation.Line)
                    && claimed.Add(d));

            if(match is null)
            {
                var where = expectation.Line is { } line ? $" on line {line}" : " anywhere in the file";
                yield return $"{name}: expected {expectation.Id}{where}, reported: {Describe(errors)}";
            }
        }

        foreach(var unexpected in errors.Where(d => !claimed.Contains(d)))
        {
            yield return
                $"{name}: unexpected {unexpected.Id.Value} on line {unexpected.Location.Start.Line} -- "
                + $"{unexpected.Message}";
        }
    }

    private static string Describe(List<Diagnostic> errors)
    {
        return errors.Count == 0
            ? "nothing"
            : string.Join(", ", errors.Select(d => $"{d.Id.Value}@{d.Location.Start.Line}"));
    }

    /// <summary>
    /// THE REAL PIPELINE, not a probe over wrapped statements: a case about a declaration, a
    /// constructor's paths or a whole type's conformance has no statement to be wrapped into, and the
    /// refusal a user gets is the one this ledger is for. Emission is never reached -- a refused
    /// program stops at the gate after Stage 5 -- so the Roslyn cost is only paid by a case that
    /// failed to be refused, which is a failing case anyway.
    /// </summary>
    private ImmutableArray<Diagnostic> Compile(string casePath)
    {
        var caseDir = Path.Combine(scratchDir, Path.GetFileNameWithoutExtension(casePath));
        _ = Directory.CreateDirectory(caseDir);

        var sourcePath = Path.Combine(caseDir, Path.GetFileName(casePath));
        File.Copy(casePath, sourcePath, overwrite: true);

        var driver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        return driver.Compile(new CompilerOptions(
            ProjectFolder: caseDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(caseDir, "out"),
            BinDirectory: Path.Combine(caseDir, "bin"),
            ProjectName: "Refusal",
            NamespaceRoot: "Refusal",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            Pipeline: PipelineMode.Experimental)).Diagnostics;
    }

    private readonly record struct Expectation(string Id, int? Line);

    private static string FindRepoRoot()
    {
        for(var dir = AppContext.BaseDirectory; dir is not null; dir = Directory.GetParent(dir)?.FullName)
        {
            if(File.Exists(Path.Combine(dir, "Mist.slnx")))
            {
                return dir;
            }
        }

        throw new InvalidOperationException("Could not locate the repository root from the test assembly.");
    }
}