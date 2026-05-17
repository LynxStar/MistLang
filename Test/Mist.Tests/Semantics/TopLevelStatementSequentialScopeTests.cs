using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Verifies that local variables declared in earlier top-level statements are
/// visible to later ones (sequential scope). All statements share the same TLS
/// scope, so a variable declared in one statement should be resolvable in
/// subsequent statements without producing "undefined variable" diagnostics.
///
/// Validates: Requirements 1.1, 1.2, 6.1, 6.2
/// </summary>
public sealed class TopLevelStatementSequentialScopeTests : IDisposable
{
    private readonly string _scratchDir;

    public TopLevelStatementSequentialScopeTests()
    {
        _scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-tls-seq-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratchDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_scratchDir))
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
    public void LaterStatement_CanReference_EarlierVariable()
    {
        // Arrange — two top-level statements where the second references
        // a variable declared in the first.
        var source = """
            var x = 1;
            var y = x;
            """;
        var filePath = Path.Combine(_scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var driver = CreateDriver();
        var options = MakeOptions(filePath);

        // Act
        var result = driver.Compile(options);

        // Assert — no error diagnostics related to name resolution.
        // Filter out MIST_TC_0017 (entry point not found) since we're testing
        // sequential scope, not entry point validation.
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value != "MIST_TC_0017")
            .ToList();

        Assert.Empty(errors);
    }

    [Fact]
    public void MultipleSequentialReferences_AllResolve()
    {
        // Arrange — a chain of variable declarations where each references the previous
        var source = """
            var a = 1;
            var b = a;
            var c = b;
            """;
        var filePath = Path.Combine(_scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var driver = CreateDriver();
        var options = MakeOptions(filePath);

        // Act
        var result = driver.Compile(options);

        // Assert — no error diagnostics related to name resolution.
        // Filter out MIST_TC_0017 (entry point not found) since we're testing
        // sequential scope, not entry point validation.
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value != "MIST_TC_0017")
            .ToList();

        Assert.Empty(errors);
    }
}
