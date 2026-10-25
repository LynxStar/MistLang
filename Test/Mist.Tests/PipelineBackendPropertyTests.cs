using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Tests.Helpers;

namespace Mist.Tests;

/// <summary>
/// Property-based tests for Pipeline short-circuit.
/// </summary>
public class PipelineBackendPropertyTests
{
    // ---------------------------------------------------------------
    //  29.1 — Pipeline short-circuit
    //  **Validates: Correctness Property 8**
    //  If the lexer reports an Error for a compilation unit, no parser
    //  diagnostic is produced for that unit.
    // ---------------------------------------------------------------

    [Fact]
    [Trait("Tier", "4")]
    public void PipelineShortCircuit_LexErrorSkipsParser()
    {
        // Use a lexer that always reports an error
        var errorLexer = new ErrorLexer();
        var parser = new MistParser();

        var driver = new CompilerDriver(errorLexer, parser, ambientTypes: _ => StubAmbientTypeSource.Instance);

        // Create a temp source file
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_test_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "test.mist");
            File.WriteAllText(sourcePath, "some source code");

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: Path.Combine(tempDir, "out"),
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProject",
                NamespaceRoot: "TestProject");

            var result = driver.Compile(options);

            // Should have lex errors
            var lexErrors = result.Diagnostics.Where(d => d.Stage == PipelineStage.Lexing).ToList();
            Assert.NotEmpty(lexErrors);

            // Should NOT have parser diagnostics (parser was skipped)
            var parseErrors = result.Diagnostics.Where(d => d.Stage == PipelineStage.Parsing).ToList();
            Assert.Empty(parseErrors);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ---------------------------------------------------------------
    //  Test doubles
    // ---------------------------------------------------------------

    /// <summary>
    /// A lexer that always reports an error diagnostic.
    /// </summary>
    private sealed class ErrorLexer : ILexer
    {
        public TokenStream Lex(SourceText source, DiagnosticBag diagnostics)
        {
            diagnostics.Add(DiagnosticFixtures.Occurrence("MIST_LEX_ERROR",
                DiagnosticSeverity.Error,
                PipelineStage.Lexing,
                "Simulated lex error for testing",
                new LinePositionSpan(new LinePosition(1, 1), new LinePosition(1, 2)),
                source.Path,
                null));

            return new TokenStream(source, ImmutableArray<Storm.Runtime.Token>.Empty);
        }
    }
}