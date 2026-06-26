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
/// Verifies that the C# emitter does NOT wrap top-level statements in a namespace
/// declaration (C# top-level statements cannot be inside a namespace), while type
/// declarations that follow the statements ARE wrapped in a namespace block.
///
/// Validates: Requirement 5.4
/// </summary>
public sealed class TopLevelStatementNamespaceEmissionTests : IDisposable
{
    private readonly string scratchDir;

    public TopLevelStatementNamespaceEmissionTests()
    {
        scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-tls-ns-emit-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// Wraps <see cref="SemanticModelBuilder"/> to capture the semantic model
    /// after the driver builds it, so we can call EmitToMemory on it.
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
    public void TopLevelStatements_AreNotWrappedInNamespace_TypeDeclarationsAre()
    {
        // Arrange — a file with a top-level statement followed by a class declaration
        var source = """
            var x = 1;
            class Foo { }
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

        // Use EmitToMemory to get the emitted C# without needing dotnet build
        Assert.NotNull(capture.CapturedModel);
        var diagnostics = new DiagnosticBag();
        var emittedFiles = CSharpEmitter.EmitToMemory(capture.CapturedModel!, projectName, diagnostics);

        // Assert — find the emitted C# for Program.mist
        Assert.True(emittedFiles.ContainsKey("Program.cs"),
            $"Expected 'Program.cs' in emitted files. Got: [{string.Join(", ", emittedFiles.Keys)}]");

        var csContent = emittedFiles["Program.cs"];

        // The top-level statement "var x = 1;" should appear in the file.
        Assert.Contains("var x = 1;", csContent);

        // The class Foo should appear inside a namespace block.
        Assert.Contains("namespace TestProject", csContent);
        Assert.Contains("class Foo", csContent);

        // Key assertion: "var x = 1;" must appear BEFORE the namespace declaration.
        // This proves the statement is NOT wrapped inside the namespace.
        var varIndex = csContent.IndexOf("var x = 1;", StringComparison.Ordinal);
        var namespaceIndex = csContent.IndexOf("namespace TestProject", StringComparison.Ordinal);
        Assert.True(varIndex < namespaceIndex,
            $"Expected 'var x = 1;' (at index {varIndex}) to appear before " +
            $"'namespace TestProject' (at index {namespaceIndex}) in the emitted C#.\n" +
            $"Emitted content:\n{csContent}");
    }

    [Fact]
    public void FileScopedNamespace_TypeDeclarationsEmittedInsideNamespaceBlock()
    {
        // Arrange — a file with a file-scoped namespace, a top-level statement, and a class.
        // The file-scoped namespace "MyApp" should be used for the type declarations,
        // NOT the inferred project namespace "TestProject".
        var source = """
            namespace MyApp;
            var x = 1;
            class Foo { }
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

        // Use EmitToMemory to get the emitted C# without needing dotnet build
        Assert.NotNull(capture.CapturedModel);
        var diagnostics = new DiagnosticBag();
        var emittedFiles = CSharpEmitter.EmitToMemory(capture.CapturedModel!, projectName, diagnostics);

        // Assert — find the emitted C# for Program.mist
        Assert.True(emittedFiles.ContainsKey("Program.cs"),
            $"Expected 'Program.cs' in emitted files. Got: [{string.Join(", ", emittedFiles.Keys)}]");

        var csContent = emittedFiles["Program.cs"];

        // The top-level statement "var x = 1;" should appear in the file.
        Assert.Contains("var x = 1;", csContent);

        // The class Foo should appear inside a "namespace MyApp" block (file-scoped namespace),
        // NOT "namespace TestProject" (the inferred project namespace).
        Assert.Contains("namespace MyApp", csContent);
        Assert.Contains("class Foo", csContent);
        Assert.DoesNotContain("namespace TestProject", csContent);

        // Key assertion: "var x = 1;" must appear BEFORE the namespace declaration.
        // This proves the statement is NOT wrapped inside the namespace.
        var varIndex = csContent.IndexOf("var x = 1;", StringComparison.Ordinal);
        var namespaceIndex = csContent.IndexOf("namespace MyApp", StringComparison.Ordinal);
        Assert.True(varIndex < namespaceIndex,
            $"Expected 'var x = 1;' (at index {varIndex}) to appear before " +
            $"'namespace MyApp' (at index {namespaceIndex}) in the emitted C#.\n" +
            $"Emitted content:\n{csContent}");

        // Verify the class is INSIDE the namespace block (between the opening { and closing })
        var openBraceIndex = csContent.IndexOf("{", namespaceIndex, StringComparison.Ordinal);
        var closeBraceIndex = csContent.LastIndexOf("}", StringComparison.Ordinal);
        var classIndex = csContent.IndexOf("class Foo", StringComparison.Ordinal);
        Assert.True(classIndex > openBraceIndex && classIndex < closeBraceIndex,
            $"Expected 'class Foo' (at index {classIndex}) to appear between " +
            $"namespace braces ({{ at {openBraceIndex}, }} at {closeBraceIndex}) in the emitted C#.\n" +
            $"Emitted content:\n{csContent}");
    }
}