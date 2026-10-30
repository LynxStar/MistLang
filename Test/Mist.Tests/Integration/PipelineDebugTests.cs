using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;

namespace Mist.Tests.Integration;

/// <summary>
/// Diagnostic tests that lex and parse whole programs and check the declarations
/// reach the namespace body of the resulting tree.
/// </summary>
[Trait("Tier", "3")]
public sealed class PipelineDebugTests
{
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
    /// Lexes and parses the HelloMist entry file, checking the AST structure at each step.
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
        var diagDump = string.Join("\n", allDiags.Select(d => $"  [{d.Severity}] {d.Number.Value}: {d.Message}"));

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
        var typeDecl = (ns.Body[0] as NamespaceTypeItemNamespaceBodyItemNode)?.TypeDeclaration as BodyTypeTypeDeclarationNode;
        Assert.NotNull(typeDecl);
        Assert.Equal("Program", typeDecl!.TypeBody.Name.Lexeme);

        // Verify Main method exists
        var mainMethod = typeDecl.TypeBody.Members
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
        var diagDump = string.Join("\n", allDiags.Select(d => $"  [{d.Severity}] {d.Number.Value}: {d.Message}"));

        Assert.True(parseResult.Root is not null,
            $"[{name}] Parse returned null root.\nDiagnostics:\n{diagDump}");

        var cu = parseResult.Root as CompilationUnitNode;
        Assert.NotNull(cu);

        var ns = cu!.GetNamespace() as FileScopedNamespaceNamespaceDeclarationNode;
        Assert.NotNull(ns);
        Assert.True(ns!.Body.Length > 0,
            $"[{name}] Namespace body is empty!\nDiagnostics:\n{diagDump}");
    }
}