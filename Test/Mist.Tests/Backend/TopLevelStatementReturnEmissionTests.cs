using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Verifies that `return &lt;int&gt;` in top-level statements emits as `return &lt;int&gt;;`
/// in the generated C#. C# top-level statements support this for setting the process exit code.
///
/// Validates: Requirements 5.1, 7.1
/// </summary>
public sealed class TopLevelStatementReturnEmissionTests : IDisposable
{
    private readonly string scratchDir;

    public TopLevelStatementReturnEmissionTests()
    {
        scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-tls-return-emit-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); }
            catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

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
            var model = inner.Build(compilation, asts, ambientTypeResolver, namespaceRoot, projectFolder, diagnostics, directReferencePaths);
            CapturedModel = model;
            return model;
        }
    }

    private static CompilerDriver CreateDriverWithCapture(CapturingSemanticModelBuilder capture)
    {
        return new CompilerDriver(
            new MistLexer(),
            new MistParser(),
            capture,
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));
    }

    [Fact]
    public void ReturnWithIntegerLiteral_InTopLevelStatement_EmitsAsReturnInt()
    {
        // Arrange — a TLS file with `return 42;`
        var source = """
            return 42;
            """;
        var filePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        const string projectName = "TestProject";
        var options = new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(scratchDir, "output"),
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: projectName,
            NamespaceRoot: projectName,
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);

        // Act — compile to build the semantic model
        _ = driver.Compile(options);

        // Use EmitToMemory to get the emitted C#
        Assert.NotNull(capture.CapturedModel);
        var diagnostics = new DiagnosticBag();
        var emittedFiles = CSharpEmitter.EmitToMemory(capture.CapturedModel!, diagnostics);

        // Assert — the emitted C# should contain `return 42;`
        Assert.True(emittedFiles.ContainsKey("Program.cs"),
            $"Expected 'Program.cs' in emitted files. Got: [{string.Join(", ", emittedFiles.Keys)}]");

        var csContent = emittedFiles["Program.cs"];

        // The emitted C# should contain `return 42;` directly — C# TLS supports exit codes
        Assert.Contains("return 42;", csContent);
    }
}