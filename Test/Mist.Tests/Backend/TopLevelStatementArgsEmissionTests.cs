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
/// Verifies that references to the implicit `args` variable in top-level statements
/// emit correctly as `args` in the generated C#. Since C# top-level statements also
/// have an implicit `args` variable, no special transformation is needed.
///
/// Validates: Requirements 5.1, 6.1, 6.2
/// </summary>
public sealed class TopLevelStatementArgsEmissionTests : IDisposable
{
    private readonly string _scratchDir;

    public TopLevelStatementArgsEmissionTests()
    {
        _scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-tls-args-emit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_scratchDir))
        {
            try { Directory.Delete(_scratchDir, recursive: true); }
            catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder _inner = new();

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
            var model = _inner.Build(compilation, asts, ambientTypeResolver, namespaceRoot, projectFolder, diagnostics, directReferencePaths);
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
    public void ArgsReference_InTopLevelStatement_EmitsAsArgs()
    {
        // Arrange — a TLS file that references the implicit `args` variable
        var source = """
            var a = args;
            """;
        var filePath = Path.Combine(_scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        const string projectName = "TestProject";
        var options = new CompilerOptions(
            ProjectFolder: _scratchDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(_scratchDir, "output"),
            BinDirectory: Path.Combine(_scratchDir, "bin"),
            ProjectName: projectName,
            NamespaceRoot: projectName,
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);

        // Act — compile to build the semantic model
        driver.Compile(options);

        // Use EmitToMemory to get the emitted C# without needing dotnet build
        Assert.NotNull(capture.CapturedModel);
        var diagnostics = new DiagnosticBag();
        var emittedFiles = CSharpEmitter.EmitToMemory(capture.CapturedModel!, projectName, diagnostics);

        // Assert — the emitted C# should contain `args`
        Assert.True(emittedFiles.ContainsKey("Program.cs"),
            $"Expected 'Program.cs' in emitted files. Got: [{string.Join(", ", emittedFiles.Keys)}]");

        var csContent = emittedFiles["Program.cs"];

        // The emitted C# should reference `args` directly — C# TLS also has implicit args
        Assert.Contains("args", csContent);

        // Specifically, the assignment should emit as `var a = args;`
        Assert.Contains("var a = args;", csContent);
    }
}