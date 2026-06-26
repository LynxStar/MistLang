using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Integration;

/// <summary>
/// Diagnostic tests to trace the full pipeline and identify why the CLI
/// fails with MIST_TC_0017 even though parser unit tests pass.
/// </summary>
public sealed class PipelineDebugTests : IDisposable
{
    private readonly string _outputDir;

    public PipelineDebugTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistDebug_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { }
        }

        GC.SuppressFinalize(this);
    }

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while(dir != null)
        {
            if(File.Exists(Path.Combine(dir, "Mist.slnx")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root (Mist.slnx)");
    }

    /// <summary>
    /// Mimics the CompilerDriver pipeline step by step, checking the AST
    /// structure at each stage to identify where symbols are lost.
    /// </summary>
    [Fact]
    public void FullPipeline_ProgramMist_ParsedAstHasNonEmptyNamespaceBody()
    {
        var sampleDir = Path.Combine(FindRepoRoot(), "samples", "HelloMist");
        var programFile = Path.Combine(sampleDir, "Program.mist");
        var contents = File.ReadAllText(programFile);
        var sourceText = new SourceText(programFile, contents);

        // Step 1: Lex
        var lexer = new Mist.Lexing.MistLexer();
        var lexBag = new DiagnosticBag();
        var tokenStream = lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, $"Lexing failed: {string.Join(", ", lexBag.ToImmutableArray().Select(d => d.Message))}");

        // Dump token stream for debugging
        var stormTokenStream = new Storm.Runtime.TokenStream(
            new Storm.Runtime.SourceText(sourceText.Path, sourceText.Contents),
            tokenStream.AllTokens);
        var defaultTokens = stormTokenStream.DefaultChannelTokens;
        var tokenDump = string.Join("\n", defaultTokens.Take(30).Select((t, i) =>
            $"  [{i}] Kind={(TokenKind)t.Kind} Lexeme='{t.Lexeme}'"));

        // Step 2: Parse
        var parser = new Mist.Parsing.MistParser();
        var parseBag = new DiagnosticBag();
        var parseResult = parser.Parse(tokenStream, parseBag);

        // Check for parse errors
        var allDiags = parseBag.ToImmutableArray();
        var parseErrors = allDiags.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var diagDump = string.Join("\n", allDiags.Select(d => $"  [{d.Severity}] {d.Id.Value}: {d.Message}"));

        Assert.True(parseResult.Root is not null,
            $"Parse returned null root.\nDiagnostics:\n{diagDump}\nFirst 30 tokens:\n{tokenDump}");

        // Step 3: Inspect the AST structure
        var cu = parseResult.Root as CompilationUnitNode;
        Assert.NotNull(cu);

        // Check namespace
        Assert.NotNull(cu!.GetNamespace());
        var ns = cu.GetNamespace() as FileScopedNamespaceNamespaceDeclarationNode;
        Assert.NotNull(ns);

        // THIS IS THE KEY CHECK: the namespace body should contain the Program class
        Assert.True(ns!.Body.Length > 0,
            $"FileScopedNamespace.Body is empty! Namespace type: {cu.GetNamespace()!.GetType().Name}. " +
            $"CompilationUnit.GetTypes().Length: {cu.GetTypes().Length}. " +
            $"Diagnostics:\n{diagDump}\nFirst 30 tokens:\n{tokenDump}");

        // Verify the class is there
        var typeDecl = (ns.Body[0] as NamespaceTypeItemNamespaceBodyItemNode)?.TypeDeclaration as ClassTypeTypeDeclarationNode;
        Assert.NotNull(typeDecl);
        Assert.Equal("Program", typeDecl!.ClassDeclaration.Name.Lexeme);

        // Verify Main method exists
        var mainMethod = typeDecl.ClassDeclaration.Members
            .OfType<MethodMemberMemberDeclarationNode>()
            .FirstOrDefault(m => m.Method.Name.Lexeme == "Main");
        Assert.NotNull(mainMethod);
    }

    /// <summary>
    /// Test with a minimal file that matches the unit test pattern to see if
    /// the issue is file-specific or general.
    /// </summary>
    [Fact]
    public void MinimalProgramMist_ParsedAstHasNonEmptyNamespaceBody()
    {
        // This is the exact same source that passes in unit tests
        var contents = "using Standard;\n\nnamespace HelloMist;\n\npublic class Program\n{\n    public static void Main(string[] args)\n    {\n    }\n}";
        var sourceText = new SourceText("test.mist", contents);

        var lexer = new Mist.Lexing.MistLexer();
        var lexBag = new DiagnosticBag();
        var tokenStream = lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors);

        var parser = new Mist.Parsing.MistParser();
        var parseBag = new DiagnosticBag();
        var parseResult = parser.Parse(tokenStream, parseBag);

        Assert.NotNull(parseResult.Root);
        var cu = parseResult.Root as CompilationUnitNode;
        Assert.NotNull(cu);

        var ns = cu!.GetNamespace() as FileScopedNamespaceNamespaceDeclarationNode;
        Assert.NotNull(ns);
        Assert.True(ns!.Body.Length > 0, "Minimal source: namespace body is empty!");
    }

    /// <summary>
    /// Incrementally add complexity to find which construct breaks parsing.
    /// </summary>
    [Theory]
    [InlineData("WithInterpolatedString",
        "using Standard;\nnamespace HelloMist;\npublic class Program {\n    public static void Main(string[] args) {\n        Console.WriteLine($\"Hello {args}\");\n    }\n}")]
    [InlineData("WithIfElse",
        "using Standard;\nnamespace HelloMist;\npublic class Program {\n    public static void Main(string[] args) {\n        if (true) { } else { }\n    }\n}")]
    [InlineData("WithForLoop",
        "using Standard;\nnamespace HelloMist;\npublic class Program {\n    public static void Main(string[] args) {\n        for (int i = 0; i < 5; i = i + 1) { }\n    }\n}")]
    [InlineData("WithWhileLoop",
        "using Standard;\nnamespace HelloMist;\npublic class Program {\n    public static void Main(string[] args) {\n        while (true) { break; }\n    }\n}")]
    [InlineData("WithTwoMethods",
        "using Standard;\nnamespace HelloMist;\npublic class Program {\n    private static int Factorial(int n) { return 1; }\n    public static void Main(string[] args) { }\n}")]
    [InlineData("WithFieldAndMethod",
        "using Standard;\nnamespace HelloMist;\npublic class Program {\n    private int x;\n    public static void Main(string[] args) { }\n}")]
    [InlineData("WithVarDecl",
        "using Standard;\nnamespace HelloMist;\npublic class Program {\n    public static void Main(string[] args) {\n        var x = 10;\n    }\n}")]
    [InlineData("WithNewObject",
        "using Standard;\nnamespace HelloMist;\npublic class Program {\n    public static void Main(string[] args) {\n        var p = new Point(3, 4);\n    }\n}")]
    [InlineData("WithForeach",
        "using Standard;\nnamespace HelloMist;\npublic class Program {\n    public static void Main(string[] args) {\n        foreach (int n in args) { }\n    }\n}")]
    [InlineData("WithArrayCreation",
        "using Standard;\nnamespace HelloMist;\npublic class Program {\n    public static void Main(string[] args) {\n        int[] numbers = new int[5];\n    }\n}")]
    public void IncrementalComplexity_NamespaceBodyNotEmpty(string name, string contents)
    {
        var sourceText = new SourceText("test.mist", contents);

        var lexer = new Mist.Lexing.MistLexer();
        var lexBag = new DiagnosticBag();
        var tokenStream = lexer.Lex(sourceText, lexBag);

        var lexErrors = lexBag.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        var parser = new Mist.Parsing.MistParser();
        var parseBag = new DiagnosticBag();
        var parseResult = parser.Parse(tokenStream, parseBag);

        var allDiags = parseBag.ToImmutableArray();
        var diagDump = string.Join("\n", allDiags.Select(d => $"  [{d.Severity}] {d.Id.Value}: {d.Message}"));

        Assert.True(parseResult.Root is not null,
            $"[{name}] Parse returned null root.\nDiagnostics:\n{diagDump}");

        var cu = parseResult.Root as CompilationUnitNode;
        Assert.NotNull(cu);

        var ns = cu!.GetNamespace() as FileScopedNamespaceNamespaceDeclarationNode;
        Assert.NotNull(ns);
        Assert.True(ns!.Body.Length > 0,
            $"[{name}] Namespace body is empty!\nDiagnostics:\n{diagDump}");
    }

    /// <summary>
    /// Tests the full pipeline from parsing through semantic model building
    /// to verify that symbols are properly registered.
    /// </summary>
    [Fact]
    public void FullPipeline_AllSampleFiles_SemanticModelHasClassAndMethodSymbols()
    {
        var sampleDir = Path.Combine(FindRepoRoot(), "samples", "HelloMist");
        var sourceFiles = new[]
        {
            Path.Combine(sampleDir, "Program.mist"),
            Path.Combine(sampleDir, "Point.mist"),
            Path.Combine(sampleDir, "Color.mist"),
        };

        var lexer = new Mist.Lexing.MistLexer();
        var parser = new Mist.Parsing.MistParser();

        var asts = ImmutableArray.CreateBuilder<Storm.Runtime.SyntaxNode>();
        var units = ImmutableArray.CreateBuilder<CompilationUnit>();

        foreach(var file in sourceFiles)
        {
            var contents = File.ReadAllText(file);
            var sourceText = new SourceText(file, contents);
            units.Add(new CompilationUnit(sourceText));

            var lexBag = new DiagnosticBag();
            var tokenStream = lexer.Lex(sourceText, lexBag);
            Assert.False(lexBag.HasErrors, $"Lexing failed for {file}");

            var parseBag = new DiagnosticBag();
            var parseResult = parser.Parse(tokenStream, parseBag);

            var parseErrors = parseBag.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if(parseErrors.Any())
            {
                Assert.Fail($"Parse errors for {file}: {string.Join(", ", parseErrors.Select(d => d.Message))}");
            }

            if(parseResult.Root is not null)
            {
                asts.Add(parseResult.Root);
            }
            else
            {
                Assert.Fail($"Parse returned null root for {file}");
            }
        }

        // Build semantic model
        var compilation = new MistCompilation(units.ToImmutable());
        var builder = new SemanticModelBuilder();
        var resolver = new CSharpAmbientTypeResolver();
        var semanticBag = new DiagnosticBag();
        var model = builder.Build(compilation, asts.ToImmutable(), resolver, namespaceRoot: "TestProject", projectFolder: "/test", semanticBag);

        // Check that we have more than just namespace symbols
        var allSymbols = model.AllSymbols.ToList();
        var namespaceSymbols = allSymbols.Where(s => s.Kind == SymbolKind.Namespace).ToList();
        var classSymbols = allSymbols.Where(s => s.Kind == SymbolKind.Class).ToList();
        var methodSymbols = allSymbols.Where(s => s.Kind == SymbolKind.Method).ToList();

        Assert.True(classSymbols.Count > 0,
            $"No class symbols found! Total symbols: {allSymbols.Count}. " +
            $"Namespace symbols: {namespaceSymbols.Count}. " +
            $"Symbol kinds: {string.Join(", ", allSymbols.Select(s => $"{s.Kind} '{s.Name}'"))}");

        Assert.True(methodSymbols.Count > 0, "No method symbols found!");

        // Specifically check for Main
        var mainSymbol = methodSymbols.FirstOrDefault(s => s.Name == "Main");
        Assert.NotNull(mainSymbol);
    }

    /// <summary>
    /// Run the full CompilerDriver and verify it gets past MIST_TC_0017.
    /// </summary>
    [Fact]
    public void FullDriver_SampleFiles_NoEntryPointError()
    {
        var sampleDir = Path.Combine(FindRepoRoot(), "samples", "HelloMist");
        var sourceFiles = Directory.GetFiles(sampleDir, "*.mist");

        var driver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: sourceFiles.ToImmutableArray(),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var result = driver.Compile(options);

        var entryPointError = result.Diagnostics
            .FirstOrDefault(d => d.Id.Value == "MIST_TC_0017");

        Assert.Null(entryPointError);
    }
}