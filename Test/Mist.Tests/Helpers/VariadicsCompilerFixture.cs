using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Helpers;

/// <summary>
/// Shared, stateless in-process compilation fixture for the variadics feature
/// (task 1.3). All compilation runs entirely in-process with <b>zero</b> external
/// process spawns: source is compiled through a single reused
/// <see cref="CompilerDriver"/> and emit is done in-memory
/// (<see cref="CompilerOptions.EmitToDisk"/> = <c>false</c>), so no <c>dotnet</c>
/// process is started and no compiled DLL is produced or run.
/// <para>
/// The driver wires the full pipeline — <see cref="MistLexer"/> +
/// <see cref="MistParser"/> + <see cref="SemanticModelBuilder"/> +
/// <see cref="TypeCheckerAnalyzer"/> + <see cref="CSharpTranspilerBackend"/> — so
/// property tests can obtain emitted diagnostic codes for a Mist source string.
/// The driver is stateless and safe to reuse across every FsCheck iteration, per
/// the workspace test-process-spawning policy.
/// </para>
/// <para>
/// Stdlib types are loaded automatically by the driver (via
/// <c>StdlibLoader.LoadFor</c>, reading the <c>Standard/</c> folder copied next to
/// the test assembly). Tests that need a semantic model directly — for
/// constructed-type identity or binding assignments — should use
/// <see cref="StdlibTestHelper"/> rather than duplicating stdlib loading here.
/// </para>
/// </summary>
internal static class VariadicsCompilerFixture
{
    // ── Shared stateless driver ──────────────────────────────────────────────
    // A single instance is reused across all iterations. The driver is stateless
    // (the workspace test-process-spawning rule guarantees this), so sharing it
    // avoids reconstructing the pipeline — and re-lexing/re-parsing the stdlib —
    // on every compile.
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ── Serializes access to the shared driver ───────────────────────────────
    // The driver is reused to avoid re-lexing/re-parsing the stdlib on every
    // compile, but its SemanticModelBuilder and TypeCheckerAnalyzer hold mutable
    // per-compile instance state (e.g. the analyzer's currentEnclosingType). That
    // state is safe to reuse across *sequential* iterations, but xUnit runs test
    // classes in parallel, so concurrent Compile calls would corrupt it (surfacing
    // as spurious cross-type diagnostics). Guarding every compile with this lock
    // restores the sequential-access invariant the shared driver relies on.
    private static readonly object CompileGate = new();

    // Reused lexer/parser for the parse-only helper (no pipeline, no analysis).
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();

    /// <summary>
    /// Compiles a Mist source string fully in-process and returns the structured
    /// <see cref="CompilerResult"/> (including every emitted diagnostic).
    /// <para>
    /// The source is written to a unique temporary file (the driver reads sources
    /// from disk); emit is in-memory so nothing is written to an output directory
    /// and no process is spawned. The temporary directory is always cleaned up.
    /// </para>
    /// </summary>
    /// <param name="source">The Mist source text to compile.</param>
    /// <param name="outputType">
    /// Defaults to <see cref="ProjectOutputType.Library"/> so that snippets without
    /// a <c>Main</c> entry point do not trigger entry-point diagnostics.
    /// </param>
    /// <param name="projectName">The project / root-namespace name.</param>
    /// <param name="globalUsings">Namespaces implicitly imported into the source.</param>
    public static CompilerResult Compile(
        string source,
        ProjectOutputType outputType = ProjectOutputType.Library,
        string projectName = "VariadicsTest",
        params string[] globalUsings)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_variadics_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(tempDir);

        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: Path.Combine(tempDir, "out"),
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: projectName,
                NamespaceRoot: projectName,
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false,
                GlobalUsings: globalUsings.Length == 0
                    ? ImmutableArray<string>.Empty
                    : globalUsings.ToImmutableArray(),
                OutputType: outputType);

            lock(CompileGate)
            {
                return SharedDriver.Compile(options);
            }
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// Compiles <paramref name="source"/> in-process and returns just the diagnostic
    /// IDs (for example <c>MIST_VAR_0001</c>) in emission order — the common shape a
    /// property test compares against.
    /// </summary>
    public static ImmutableArray<string> CompileDiagnosticIds(
        string source,
        ProjectOutputType outputType = ProjectOutputType.Library,
        params string[] globalUsings)
    {
        var result = Compile(source, outputType, "VariadicsTest", globalUsings);
        return result.Diagnostics.Select(d => d.Id.Value).ToImmutableArray();
    }

    /// <summary>
    /// Reports whether a compilation emitted a diagnostic with the given ID.
    /// </summary>
    public static bool EmittedDiagnostic(CompilerResult result, string diagnosticId)
    {
        return result.Diagnostics.Any(d => d.Id.Value == diagnosticId);
    }

    /// <summary>
    /// Lexes and parses a Mist source string in-process (no semantic model, no
    /// analysis, no emit) and returns the parsed AST root together with every
    /// lex/parse diagnostic. Useful for AST-shape and round-trip checks that only
    /// need the front end.
    /// </summary>
    public static ParseOutcome Parse(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var diagnostics = new DiagnosticBag();

        var tokens = Lexer.Lex(sourceText, diagnostics);
        var parseResult = Parser.Parse(tokens, diagnostics);

        var all = diagnostics.ToImmutableArray().AddRange(parseResult.Diagnostics);
        return new ParseOutcome(parseResult.Root, all);
    }

    /// <summary>
    /// Returns <c>true</c> when the parse outcome contains no lex- or parse-stage
    /// error diagnostics and produced a non-null AST root.
    /// </summary>
    public static bool ParsedCleanly(ParseOutcome outcome)
    {
        if(outcome.Root is null)
        {
            return false;
        }

        return !outcome.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Stage is PipelineStage.Lexing or PipelineStage.Parsing);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if(Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup of the temporary compile directory.
        }
    }

    /// <summary>
    /// The result of a parse-only run: the AST root (null on parse failure) and the
    /// collected lex/parse diagnostics.
    /// </summary>
    internal sealed record ParseOutcome(
        Storm.Runtime.SyntaxNode? Root,
        ImmutableArray<Diagnostic> Diagnostics);
}