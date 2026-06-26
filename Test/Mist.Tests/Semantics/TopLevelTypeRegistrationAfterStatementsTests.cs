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
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Verifies that type declarations appearing after top-level statements in the same
/// file are still correctly registered in the semantic model with the correct namespace
/// (inferred from the project name or from a file-scoped namespace declaration).
///
/// This confirms that the SemanticModelBuilder's Pass 1 skip of
/// StatementItemTopLevelItemNode does not prevent subsequent type declarations from
/// being registered.
///
/// Validates: Requirements 1.4, 1.5
/// </summary>
public sealed class TopLevelTypeRegistrationAfterStatementsTests : IDisposable
{
    private readonly string scratchDir;

    public TopLevelTypeRegistrationAfterStatementsTests()
    {
        scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-tls-type-reg-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scratchDir);
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
    /// Wraps <see cref="SemanticModelBuilder"/> to capture the semantic model
    /// after the driver builds it, so we can inspect registered types.
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
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            capture,
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));
    }

    [Fact]
    public void ClassAfterTopLevelStatement_IsRegisteredWithInferredNamespace()
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
            OutputDirectory: scratchDir,
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: projectName,
            NamespaceRoot: projectName,
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);

        // Act
        _ = driver.Compile(options);

        // Assert — the class Foo is registered in the semantic model with the
        // inferred namespace (which equals the project name for a file at the root).
        Assert.NotNull(capture.CapturedModel);

        var fooSymbol = capture.CapturedModel!.AllSymbols
            .FirstOrDefault(s => s.Name == "Foo" && s.Kind == SymbolKind.Class);
        Assert.NotNull(fooSymbol);

        var fooType = Assert.IsType<ClassType>(fooSymbol!.DeclaredType);
        Assert.Equal(projectName, fooType.ContainingNamespace);
    }

    [Fact]
    public void StructAfterTopLevelStatement_IsRegisteredWithInferredNamespace()
    {
        // Arrange — a file with a top-level statement followed by a struct declaration
        var source = """
            var x = 1;
            struct Bar { }
            """;
        var filePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        const string projectName = "TestProject";
        var options = new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: scratchDir,
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: projectName,
            NamespaceRoot: projectName,
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);

        // Act
        _ = driver.Compile(options);

        // Assert — the struct Bar is registered with the inferred namespace.
        Assert.NotNull(capture.CapturedModel);

        var barSymbol = capture.CapturedModel!.AllSymbols
            .FirstOrDefault(s => s.Name == "Bar" && s.Kind == SymbolKind.Struct);
        Assert.NotNull(barSymbol);

        var barType = Assert.IsType<StructType>(barSymbol!.DeclaredType);
        Assert.Equal(projectName, barType.ContainingNamespace);
    }

    [Fact]
    public void MultipleTypesAfterTopLevelStatements_AllRegisteredWithInferredNamespace()
    {
        // Arrange — multiple top-level statements followed by multiple type declarations
        var source = """
            var x = 1;
            var y = 2;
            class Alpha { }
            struct Beta { }
            """;
        var filePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        const string projectName = "MyApp";
        var options = new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: scratchDir,
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: projectName,
            NamespaceRoot: projectName,
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);

        // Act
        _ = driver.Compile(options);

        // Assert — both types are registered with the inferred namespace.
        Assert.NotNull(capture.CapturedModel);

        var alphaSymbol = capture.CapturedModel!.AllSymbols
            .FirstOrDefault(s => s.Name == "Alpha" && s.Kind == SymbolKind.Class);
        Assert.NotNull(alphaSymbol);
        var alphaType = Assert.IsType<ClassType>(alphaSymbol!.DeclaredType);
        Assert.Equal(projectName, alphaType.ContainingNamespace);

        var betaSymbol = capture.CapturedModel!.AllSymbols
            .FirstOrDefault(s => s.Name == "Beta" && s.Kind == SymbolKind.Struct);
        Assert.NotNull(betaSymbol);
        var betaType = Assert.IsType<StructType>(betaSymbol!.DeclaredType);
        Assert.Equal(projectName, betaType.ContainingNamespace);
    }

    [Fact]
    public void FileScopedNamespace_AppliesToTypeDeclarations_NotStatements()
    {
        // Arrange — a file with a file-scoped namespace, a top-level statement, and a class.
        // The file-scoped namespace should apply to the class (registered under "MyApp")
        // but the top-level statement itself is not namespaced.
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
            OutputDirectory: scratchDir,
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: projectName,
            NamespaceRoot: projectName,
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);

        // Act
        _ = driver.Compile(options);

        // Assert — class Foo is registered under the file-scoped namespace "MyApp",
        // NOT the inferred project namespace "TestProject".
        Assert.NotNull(capture.CapturedModel);

        var fooSymbol = capture.CapturedModel!.AllSymbols
            .FirstOrDefault(s => s.Name == "Foo" && s.Kind == SymbolKind.Class);
        Assert.NotNull(fooSymbol);

        var fooType = Assert.IsType<ClassType>(fooSymbol!.DeclaredType);
        Assert.Equal("MyApp", fooType.ContainingNamespace);
    }
}