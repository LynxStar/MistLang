using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Driver;

/// <summary>
/// Verifies that <see cref="CompilerDriver.Compile"/> correctly sets
/// <see cref="CompilationUnit.HasTopLevelStatements"/> after parsing:
/// <c>true</c> for files containing top-level statements,
/// <c>false</c> for files without them.
///
/// Validates: Requirement 1.1 (top-level statement detection in the driver).
/// </summary>
public sealed class HasTopLevelStatementsFlagTests : IDisposable
{
    private readonly string scratchDir;

    public HasTopLevelStatementsFlagTests()
    {
        scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-tls-flag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Wraps <see cref="SemanticModelBuilder"/> to capture the
    /// <see cref="MistCompilation"/> after the driver updates it with
    /// the <c>HasTopLevelStatements</c> flag.
    /// </summary>
    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder inner = new();

        public ISemanticModel? CapturedModel { get; private set; }

        public ISemanticModel Build(
            MistCompilation compilation,
            ImmutableArray<Storm.Runtime.SyntaxNode> asts,
            IAmbientTypeResolver? ambientTypeResolver,
            string namespaceRoot,
            string projectFolder,
            DiagnosticBag diagnostics,
            ImmutableHashSet<string>? directReferencePaths = null)
        {
            // The compilation passed here already has HasTopLevelStatements
            // set by the driver (it updates the compilation before calling
            // the semantic model builder).
            var model = inner.Build(compilation, asts, ambientTypeResolver, namespaceRoot, projectFolder, diagnostics, directReferencePaths);
            CapturedModel = model;
            return model;
        }
    }

    private static CompilerDriver CreateDriverWithCapture(CapturingSemanticModelBuilder capture)
    {
        return new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            capture,
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));
    }

    [Fact]
    public void Compile_FileWithTopLevelStatements_SetsHasTopLevelStatementsTrue()
    {
        // Arrange — a file with a top-level statement (var declaration at file scope)
        var source = "var x = 1;\n";
        var filePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: scratchDir,
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);

        // Act
        driver.Compile(options);

        // Assert — the semantic model was built and the compilation unit
        // for our user file has HasTopLevelStatements = true.
        Assert.NotNull(capture.CapturedModel);
        var userUnit = capture.CapturedModel!.Compilation.Units
            .FirstOrDefault(u => !u.IsStdlib && u.Source.Path == filePath);
        Assert.NotNull(userUnit);
        Assert.True(userUnit!.HasTopLevelStatements,
            "Expected HasTopLevelStatements = true for a file containing 'var x = 1;' at file scope.");
    }

    [Fact]
    public void Compile_FileWithoutTopLevelStatements_SetsHasTopLevelStatementsFalse()
    {
        // Arrange — a file with only a class declaration (no top-level statements)
        var source = "class Foo { }\n";
        var filePath = Path.Combine(scratchDir, "Foo.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: scratchDir,
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);

        // Act
        driver.Compile(options);

        // Assert — the compilation unit for our user file has
        // HasTopLevelStatements = false.
        Assert.NotNull(capture.CapturedModel);
        var userUnit = capture.CapturedModel!.Compilation.Units
            .FirstOrDefault(u => !u.IsStdlib && u.Source.Path == filePath);
        Assert.NotNull(userUnit);
        Assert.False(userUnit!.HasTopLevelStatements,
            "Expected HasTopLevelStatements = false for a file containing only 'class Foo { }'.");
    }
}