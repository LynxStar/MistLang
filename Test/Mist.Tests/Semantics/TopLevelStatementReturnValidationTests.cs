using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Verifies that return statements in top-level statement files are validated
/// correctly:
///   - return with integer expression → valid (exit code)
///   - return with no expression → valid (exit 0)
///   - return with non-integer expression → type error (MIST_TLS_RETURN_TYPE)
///
/// Validates: Requirements 7.1, 7.2, 7.3
/// </summary>
public sealed class TopLevelStatementReturnValidationTests : IDisposable
{
    private readonly string _scratchDir;

    public TopLevelStatementReturnValidationTests()
    {
        _scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-tls-return-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_scratchDir))
        {
            try { Directory.Delete(_scratchDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    private static CompilerDriver CreateDriver()
    {
        return new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));
    }

    private CompilerOptions MakeOptions(string filePath)
    {
        return new CompilerOptions(
            ProjectFolder: _scratchDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _scratchDir,
            BinDirectory: Path.Combine(_scratchDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);
    }

    [Fact]
    public void Return_WithIntegerExpression_IsValid()
    {
        // Arrange — a TLS file with `return 0;`
        var source = """
            return 0;
            """;
        var filePath = Path.Combine(_scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var driver = CreateDriver();
        var options = MakeOptions(filePath);

        // Act
        var result = driver.Compile(options);

        // Assert — no MIST_TLS_RETURN_TYPE error.
        var returnTypeErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value == "MIST_TLS_RETURN_TYPE")
            .ToList();

        Assert.Empty(returnTypeErrors);
    }

    [Fact]
    public void Return_WithNoExpression_IsValid()
    {
        // Arrange — a TLS file with bare `return;`
        var source = """
            return;
            """;
        var filePath = Path.Combine(_scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var driver = CreateDriver();
        var options = MakeOptions(filePath);

        // Act
        var result = driver.Compile(options);

        // Assert — no MIST_TLS_RETURN_TYPE error.
        var returnTypeErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value == "MIST_TLS_RETURN_TYPE")
            .ToList();

        Assert.Empty(returnTypeErrors);
    }

    [Fact]
    public void Return_WithNonIntegerExpression_ProducesTypeError()
    {
        // Arrange — a TLS file with `return "hello";`
        var source = """
            return "hello";
            """;
        var filePath = Path.Combine(_scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var driver = CreateDriver();
        var options = MakeOptions(filePath);

        // Act
        var result = driver.Compile(options);

        // Assert — exactly one MIST_TLS_RETURN_TYPE error.
        var returnTypeErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value == "MIST_TLS_RETURN_TYPE")
            .ToList();

        Assert.Single(returnTypeErrors);
        Assert.Contains("integer exit code", returnTypeErrors[0].Message);
        Assert.Contains("String", returnTypeErrors[0].Message);
    }
}