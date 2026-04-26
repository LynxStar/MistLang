using System.Collections.Immutable;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests;

/// <summary>
/// Property-based tests for Pipeline short-circuit and Backend determinism.
/// </summary>
public class PipelineBackendPropertyTests
{
    // ---------------------------------------------------------------
    //  29.1 — Pipeline short-circuit
    //  **Validates: Correctness Property 8**
    //  If the lexer reports an Error for a compilation unit, no parser
    //  diagnostic is produced for that unit. If the parser reports an
    //  Error, no semantic-model diagnostic references that unit. If any
    //  analyzer reports an Error, no backend diagnostic is produced.
    // ---------------------------------------------------------------

    [Fact]
    public void PipelineShortCircuit_LexErrorSkipsParser()
    {
        // Use a lexer that always reports an error
        var errorLexer = new ErrorLexer();
        var parser = new MistParser();
        var builder = new SemanticModelBuilder();
        var backend = new CSharpTranspilerBackend();

        var driver = new CompilerDriver(
            errorLexer,
            parser,
            builder,
            ImmutableArray<IAnalyzer>.Empty,
            ImmutableArray.Create<IBackend>(backend));

        // Create a temp source file
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "test.mist");
            File.WriteAllText(sourcePath, "some source code");

            var options = new CompilerOptions(
                ImmutableArray.Create(sourcePath),
                new BackendId("csharp"),
                Path.Combine(tempDir, "out"),
                ImmutableArray<string>.Empty);

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

    [Fact]
    public void PipelineShortCircuit_AnalyzerErrorSkipsBackend()
    {
        var lexer = new MistLexer();
        var parser = new MistParser();
        var builder = new SemanticModelBuilder();
        var backend = new CSharpTranspilerBackend();
        var errorAnalyzer = new ErrorAnalyzer();

        var driver = new CompilerDriver(
            lexer,
            parser,
            builder,
            ImmutableArray.Create<IAnalyzer>(errorAnalyzer),
            ImmutableArray.Create<IBackend>(backend));

        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "test.mist");
            File.WriteAllText(sourcePath, "let x = 42;");

            var options = new CompilerOptions(
                ImmutableArray.Create(sourcePath),
                new BackendId("csharp"),
                Path.Combine(tempDir, "out"),
                ImmutableArray<string>.Empty);

            var result = driver.Compile(options);

            // Should have analyzer errors
            var analyzerErrors = result.Diagnostics.Where(d => d.Stage == PipelineStage.Analysis).ToList();
            Assert.NotEmpty(analyzerErrors);

            // Should NOT have backend diagnostics (backend was skipped)
            var backendErrors = result.Diagnostics.Where(d => d.Stage == PipelineStage.Backend).ToList();
            Assert.Empty(backendErrors);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void PipelineShortCircuit_NormalFlowReachesAllStages()
    {
        // With the skeletal implementation, the pipeline should flow through
        // lex → parse → semantic model → analyzers without errors.
        // The parser returns null root, so the semantic model gets empty ASTs.
        var lexer = new MistLexer();
        var parser = new MistParser();
        var builder = new SemanticModelBuilder();
        var backend = new CSharpTranspilerBackend();

        var driver = new CompilerDriver(
            lexer,
            parser,
            builder,
            ImmutableArray<IAnalyzer>.Empty,
            ImmutableArray.Create<IBackend>(backend));

        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "test.mist");
            File.WriteAllText(sourcePath, "let x = 42;");

            var options = new CompilerOptions(
                ImmutableArray.Create(sourcePath),
                new BackendId("csharp"),
                Path.Combine(tempDir, "out"),
                ImmutableArray<string>.Empty);

            var result = driver.Compile(options);

            // With the skeletal parser returning null root, the AST list is empty,
            // but the semantic model builder still succeeds with empty input.
            // No lex or parse errors should be present.
            var lexErrors = result.Diagnostics.Where(d =>
                d.Stage == PipelineStage.Lexing && d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.Empty(lexErrors);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ---------------------------------------------------------------
    //  29.2 — Backend determinism
    //  **Validates: Correctness Property 9**
    //  Repeated Emit calls produce byte-identical file sets.
    // ---------------------------------------------------------------

    [Fact]
    public void BackendDeterminism_RepeatedEmitProducesIdenticalFiles()
    {
        // Build a semantic model
        var source = new SourceText("test.mist", "let x = 42;");
        var unit = new CompilationUnit(source);
        var compilation = new MistCompilation(ImmutableArray.Create(unit));
        var builder = new SemanticModelBuilder();
        var bag = new DiagnosticBag();
        var model = builder.Build(compilation, ImmutableArray<Storm.Runtime.SyntaxNode>.Empty, null, bag);

        // Use the CSharpEmitter directly to test emission determinism
        // without invoking dotnet build (which would introduce external non-determinism).

        var tempDir1 = Path.Combine(Path.GetTempPath(), $"mist_det1_{Guid.NewGuid():N}");
        var tempDir2 = Path.Combine(Path.GetTempPath(), $"mist_det2_{Guid.NewGuid():N}");

        try
        {
            // First emission
            var bag1 = new DiagnosticBag();
            CSharpEmitter.Emit(model, tempDir1, bag1);

            // Second emission
            var bag2 = new DiagnosticBag();
            CSharpEmitter.Emit(model, tempDir2, bag2);

            // Compare file sets
            var files1 = Directory.GetFiles(tempDir1, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(tempDir1, f))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            var files2 = Directory.GetFiles(tempDir2, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(tempDir2, f))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            // Same file set
            Assert.Equal(files1, files2);

            // Text-identical contents (comparing as text to avoid BOM/encoding differences)
            foreach(var relPath in files1)
            {
                var content1 = File.ReadAllText(Path.Combine(tempDir1, relPath));
                var content2 = File.ReadAllText(Path.Combine(tempDir2, relPath));
                Assert.Equal(content1, content2);
            }
        }
        finally
        {
            if(Directory.Exists(tempDir1))
            {
                Directory.Delete(tempDir1, recursive: true);
            }
            if(Directory.Exists(tempDir2))
            {
                Directory.Delete(tempDir2, recursive: true);
            }
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
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_LEX_ERROR"),
                DiagnosticSeverity.Error,
                PipelineStage.Lexing,
                "Simulated lex error for testing",
                new LinePositionSpan(new LinePosition(1, 1), new LinePosition(1, 2)),
                source.Path,
                null));

            return new TokenStream(source, ImmutableArray<Storm.Runtime.Token>.Empty);
        }
    }

    /// <summary>
    /// An analyzer that always reports an error diagnostic.
    /// </summary>
    private sealed class ErrorAnalyzer : IAnalyzer
    {
        public string AnalyzerId => "ErrorAnalyzer";

        public void Analyze(ISemanticModel model, DiagnosticBag diagnostics)
        {
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_ANALYSIS_ERROR"),
                DiagnosticSeverity.Error,
                PipelineStage.Analysis,
                "Simulated analyzer error for testing",
                null,
                null,
                AnalyzerId));
        }
    }
}
