// Feature: structured-context — Unit tests for pretty printer handling of context constructs
// **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5, 11.6**

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Unit tests verifying that the pretty printer correctly formats all structured context constructs.
/// Uses in-process MistParser and MistPrettyPrinter — no process spawning.
/// </summary>
public class WithStatementPrettyPrinterTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");

        return Assert.IsType<CompilationUnitNode>(result.Root);
    }

    private static string PrettyPrint(string source)
    {
        var cu = ParseOrFail(source);
        return MistPrettyPrinter.Print(cu, source);
    }

    // ─── Requirement 11.1: with var identifier = expression ; ────────────

    [Fact]
    public void WithVarDeclaration_PrettyPrintsCorrectly()
    {
        const string source = "class T { public void M() { with var x = 42; } }";

        var printed = PrettyPrint(source);

        Assert.Contains("with var x = 42;", printed);
    }

    [Fact]
    public void WithVarDeclaration_ComplexExpression_PrettyPrintsCorrectly()
    {
        const string source = "class T { public void M() { with var ctx = new MyContext(); } }";

        var printed = PrettyPrint(source);

        Assert.Contains("with var ctx = new MyContext();", printed);
    }

    [Fact]
    public void WithVarDeclaration_RoundTripIsStable()
    {
        const string source = "class T { public void M() { with var x = 42; } }";

        var ast1 = ParseOrFail(source);
        var printed1 = MistPrettyPrinter.Print(ast1, source);

        var ast2 = ParseOrFail(printed1);
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        Assert.Equal(printed1, printed2);
    }

    // ─── Requirement 11.2: with TypeReference identifier = expression ; ──

    [Fact]
    public void WithTypedDeclaration_PrettyPrintsCorrectly()
    {
        const string source = "class T { public void M() { with MyType x = 42; } }";

        var printed = PrettyPrint(source);

        Assert.Contains("with MyType x = 42;", printed);
    }

    [Fact]
    public void WithTypedDeclaration_PrimitiveType_PrettyPrintsCorrectly()
    {
        const string source = "class T { public void M() { with int count = 5; } }";

        var printed = PrettyPrint(source);

        Assert.Contains("with int count = 5;", printed);
    }

    [Fact]
    public void WithTypedDeclaration_RoundTripIsStable()
    {
        const string source = "class T { public void M() { with MyType x = 42; } }";

        var ast1 = ParseOrFail(source);
        var printed1 = MistPrettyPrinter.Print(ast1, source);

        var ast2 = ParseOrFail(printed1);
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        Assert.Equal(printed1, printed2);
    }

    // ─── Requirement 11.3: with TypeReference _ = expression ; ───────────

    [Fact]
    public void WithDiscardDeclaration_PrettyPrintsCorrectly()
    {
        const string source = "class T { public void M() { with MyType _ = 42; } }";

        var printed = PrettyPrint(source);

        Assert.Contains("with MyType _ = 42;", printed);
    }

    [Fact]
    public void WithDiscardDeclaration_PrimitiveType_PrettyPrintsCorrectly()
    {
        const string source = "class T { public void M() { with string _ = \"hello\"; } }";

        var printed = PrettyPrint(source);

        Assert.Contains("with string _ = \"hello\";", printed);
    }

    [Fact]
    public void WithDiscardDeclaration_RoundTripIsStable()
    {
        const string source = "class T { public void M() { with MyType _ = 42; } }";

        var ast1 = ParseOrFail(source);
        var printed1 = MistPrettyPrinter.Print(ast1, source);

        var ast2 = ParseOrFail(printed1);
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        Assert.Equal(printed1, printed2);
    }

    // ─── Requirement 11.4: with expression ; ─────────────────────────────

    [Fact]
    public void WithPushStatement_PrettyPrintsCorrectly()
    {
        const string source = "class T { public void M() { with someExpr; } }";

        var printed = PrettyPrint(source);

        Assert.Contains("with someExpr;", printed);
    }

    [Fact]
    public void WithPushStatement_MethodCall_PrettyPrintsCorrectly()
    {
        const string source = "class T { public void M() { with getContext(); } }";

        var printed = PrettyPrint(source);

        Assert.Contains("with getContext();", printed);
    }

    [Fact]
    public void WithPushStatement_RoundTripIsStable()
    {
        const string source = "class T { public void M() { with someExpr; } }";

        var ast1 = ParseOrFail(source);
        var printed1 = MistPrettyPrinter.Print(ast1, source);

        var ast2 = ParseOrFail(printed1);
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        Assert.Equal(printed1, printed2);
    }

    // ─── Requirement 11.5: with (expression) { ... } ─────────────────────

    [Fact]
    public void WithBlockStatement_PrettyPrintsCorrectly()
    {
        const string source = "class T { public void M() { with(someExpr) { } } }";

        var printed = PrettyPrint(source);

        Assert.Contains("with (someExpr) {", printed);
    }

    [Fact]
    public void WithBlockStatement_WithBody_PrettyPrintsCorrectly()
    {
        const string source = "class T { public void M() { with(ctx) { var x = 1; } } }";

        var printed = PrettyPrint(source);

        Assert.Contains("with (ctx) {", printed);
        Assert.Contains("var x = 1;", printed);
    }

    [Fact]
    public void WithBlockStatement_RoundTripIsStable()
    {
        const string source = "class T { public void M() { with(someExpr) { var x = 1; } } }";

        var ast1 = ParseOrFail(source);
        var printed1 = MistPrettyPrinter.Print(ast1, source);

        var ast2 = ParseOrFail(printed1);
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        Assert.Equal(printed1, printed2);
    }

    // ─── Requirement 11.6: Method with clauses ───────────────────────────

    [Fact]
    public void MethodWithClause_PrettyPrintsCorrectly()
    {
        const string source = "class T { public void M() with MyType ctx { } }";

        var printed = PrettyPrint(source);

        Assert.Contains("void M() with MyType ctx {", printed);
    }

    [Fact]
    public void MethodWithMultipleClauses_PrettyPrintsCorrectly()
    {
        const string source = "class T { public void M() with MyType ctx with OtherType other { } }";

        var printed = PrettyPrint(source);

        Assert.Contains("void M() with MyType ctx with OtherType other {", printed);
    }

    [Fact]
    public void MethodWithClause_RoundTripIsStable()
    {
        const string source = "class T { public void M() with MyType ctx { } }";

        var ast1 = ParseOrFail(source);
        var printed1 = MistPrettyPrinter.Print(ast1, source);

        var ast2 = ParseOrFail(printed1);
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        Assert.Equal(printed1, printed2);
    }

    [Fact]
    public void ConstructorWithClause_PrettyPrintsCorrectly()
    {
        const string source = "class T { T() with MyType ctx { } }";

        var printed = PrettyPrint(source);

        Assert.Contains("T() with MyType ctx {", printed);
    }

    [Fact]
    public void ConstructorWithClause_RoundTripIsStable()
    {
        const string source = "class T { T() with MyType ctx { } }";

        var ast1 = ParseOrFail(source);
        var printed1 = MistPrettyPrinter.Print(ast1, source);

        var ast2 = ParseOrFail(printed1);
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        Assert.Equal(printed1, printed2);
    }

    [Fact]
    public void AbstractMethodWithClause_PrettyPrintsCorrectly()
    {
        const string source = "abstract I { void M() with MyType ctx; }";

        var printed = PrettyPrint(source);

        Assert.Contains("void M() with MyType ctx;", printed);
    }

    [Fact]
    public void AbstractMethodWithClause_RoundTripIsStable()
    {
        const string source = "abstract I { void M() with MyType ctx; }";

        var ast1 = ParseOrFail(source);
        var printed1 = MistPrettyPrinter.Print(ast1, source);

        var ast2 = ParseOrFail(printed1);
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        Assert.Equal(printed1, printed2);
    }

    [Fact]
    public void TraitMethodWithClause_PrettyPrintsCorrectly()
    {
        const string source = "trait T { void M() with MyType ctx; }";

        var printed = PrettyPrint(source);

        Assert.Contains("void M() with MyType ctx;", printed);
    }

    [Fact]
    public void TraitMethodWithClause_RoundTripIsStable()
    {
        const string source = "trait T { void M() with MyType ctx; }";

        var ast1 = ParseOrFail(source);
        var printed1 = MistPrettyPrinter.Print(ast1, source);

        var ast2 = ParseOrFail(printed1);
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        Assert.Equal(printed1, printed2);
    }
}
