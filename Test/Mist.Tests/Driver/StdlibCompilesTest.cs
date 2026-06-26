using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Driver;

/// <summary>
/// Task 27 smoke test for the Mist Standard Library spec: loads every
/// <c>*.mist</c> file under the repo's <c>Source/Standard/</c> directory via
/// <see cref="StdlibLoader.LoadFrom"/>, runs each unit through
/// lex → parse → <see cref="SemanticModelBuilder.Build"/>, and asserts that
/// the combined diagnostic bag contains zero diagnostics of severity
/// <see cref="DiagnosticSeverity.Error"/>. A stdlib file that fails to lex,
/// parse, or semantically validate will trip this test before ever reaching
/// the driver (Task 28). The test relies on the fact that
/// <see cref="CompilationUnit.IsStdlib"/> is set to <c>true</c> by the loader,
/// so <c>Pass1e_ValidateIntrinsicMarkers</c>' <c>MIST_SB_0031</c> suppression
/// (member and type-level <c>intrinsic</c> only permitted in stdlib units)
/// takes effect.
///
/// Validates: Requirements 1.4, 1.5, 2.4.
/// </summary>
public sealed class StdlibCompilesTest
{
    /// <summary>
    /// Walks up from the test assembly's output directory
    /// (typically <c>Test/Mist.Tests/bin/Debug/net10.0/</c>) until a directory
    /// containing <c>Mist.slnx</c> is found. Mirrors the pattern used by
    /// other integration tests (<c>EndToEndCompilationTests.FindRepoRoot</c>,
    /// <c>PipelineDebugTests.FindRepoRoot</c>).
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while(dir is not null)
        {
            if(File.Exists(Path.Combine(dir, "Mist.slnx")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(
            "Could not find repository root (Mist.slnx) from AppContext.BaseDirectory.");
    }

    [Fact]
    public void Stdlib_LexParseAndBuildSemanticModel_ProducesNoErrorDiagnostics()
    {
        var repoRoot = FindRepoRoot();
        var stdlibRoot = Path.Combine(repoRoot, "Source", "Standard");
        Assert.True(
            Directory.Exists(stdlibRoot),
            $"Stdlib source root not found: {stdlibRoot}");

        // ── Step 1: load every stdlib .mist file as a CompilationUnit with
        //           IsStdlib=true (exactly what StdlibLoader.LoadFor would do
        //           at runtime).
        var loaderDiagnostics = new DiagnosticBag();
        var units =
            StdlibLoader.LoadFrom(stdlibRoot, loaderDiagnostics);
        Assert.Empty(loaderDiagnostics.ToImmutableArray());
        Assert.NotEmpty(units);
        Assert.All(units, u => Assert.True(u.IsStdlib, $"Expected IsStdlib=true on {u.Source.Path}"));

        // ── Step 2: lex and parse each unit, accumulating ASTs and reporting
        //           any lex/parse errors up front.
        ILexer lexer = new Mist.Lexing.MistLexer();
        IParser parser = new Mist.Parsing.MistParser();

        var pipelineDiagnostics = new DiagnosticBag();
        var asts = ImmutableArray.CreateBuilder<Storm.Runtime.SyntaxNode>(units.Length);

        foreach(var unit in units)
        {
            var tokens = lexer.Lex(unit.Source, pipelineDiagnostics);
            var parseResult = parser.Parse(tokens, pipelineDiagnostics);
            Assert.NotNull(parseResult.Root);
            asts.Add(parseResult.Root!);
        }

        // ── Step 3: build the semantic model across every stdlib unit in one
        //           go so type references across stdlib files resolve.
        var compilation = new MistCompilation(units);
        var builder = new SemanticModelBuilder();
        _ = builder.Build(
            compilation,
            asts.ToImmutable(),
            ambientTypeResolver: null,
            namespaceRoot: "Standard",
            projectFolder: stdlibRoot,
            pipelineDiagnostics);

        // ── Step 4: assert — the combined bag contains zero Error-severity
        //           diagnostics. Warnings and below are permitted.
        var errors = pipelineDiagnostics
            .ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();

        Assert.True(
            errors.Length == 0,
            "Expected zero Error-severity diagnostics from the stdlib pipeline, but got:\n"
                + string.Join(
                    "\n",
                    errors.Select(d => $"  [{d.Id.Value}] {d.CompilationUnitPath}: {d.Message}")));
    }
}