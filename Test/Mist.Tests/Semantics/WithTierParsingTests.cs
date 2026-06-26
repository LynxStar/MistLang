// Task 1.3 — Unit tests for `with {tier}` parsing on method declarations.
//
// Validates that the parser + SemanticModelBuilder correctly produce a DeclaredThisTier
// annotation on method symbols when a `with {tier}` clause is present, and that methods
// without the clause retain a null DeclaredThisTier.
//
// Uses in-process lex → parse → semantic model build (no process spawning).
//
// Validates: Requirements 17.8, 17.9

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for the <c>with {tier}</c> clause on method declarations (Task 1.3).
/// Asserts that the parser accepts the syntax and the <see cref="SemanticModelBuilder"/>
/// maps it to the correct <see cref="Symbol.DeclaredThisTier"/> value.
///
/// <b>Validates: Requirements 17.8, 17.9</b>
/// </summary>
public sealed class WithTierParsingTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private readonly SemanticModelBuilder builder = new();

    /// <summary>
    /// Lexes, parses, and builds a semantic model from the given Mist source.
    /// Asserts no lex or parse errors are produced.
    /// </summary>
    private ISemanticModel BuildModel(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, "Lex should not produce errors");

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, $"Parse should not produce errors. Errors: {string.Join(", ", parseBag.ToImmutableArray().Select(d => d.Message))}");
        Assert.NotNull(parseResult.Root);

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        return builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);
    }

    /// <summary>
    /// Finds a method symbol by name in the semantic model and returns it cast to <see cref="Symbol"/>.
    /// </summary>
    private static Symbol FindMethod(ISemanticModel model, string methodName)
    {
        return (Symbol)model.AllSymbols.First(s => s.Name == methodName && s.Kind == SymbolKind.Method);
    }

    // ─── with borrowed ───────────────────────────────────────────────────

    [Fact]
    public void WithBorrowed_ParsesSuccessfully_AndProducesCorrectAnnotation()
    {
        var source = @"
class Foo
{
    void DoWork() with borrowed { }
}
";
        var model = BuildModel(source);
        var method = FindMethod(model, "DoWork");

        Assert.Equal(ThisTier.Borrowed, method.DeclaredThisTier);
    }

    // ─── with frozen ─────────────────────────────────────────────────────

    [Fact]
    public void WithFrozen_ParsesSuccessfully_AndProducesCorrectAnnotation()
    {
        var source = @"
class Foo
{
    string Describe() with frozen { return """"; }
}
";
        var model = BuildModel(source);
        var method = FindMethod(model, "Describe");

        Assert.Equal(ThisTier.Frozen, method.DeclaredThisTier);
    }

    // ─── with owned ──────────────────────────────────────────────────────

    [Fact]
    public void WithOwned_ParsesSuccessfully_AndProducesCorrectAnnotation()
    {
        var source = @"
class Foo
{
    void Consume() with owned { }
}
";
        var model = BuildModel(source);
        var method = FindMethod(model, "Consume");

        Assert.Equal(ThisTier.Owned, method.DeclaredThisTier);
    }

    // ─── with readonly ref (two-token tier) ──────────────────────────────

    [Fact]
    public void WithReadonlyRef_TwoTokenTier_ParsesSuccessfully_AndProducesCorrectAnnotation()
    {
        var source = @"
class Foo
{
    void Bar() with readonly ref { }
}
";
        var model = BuildModel(source);
        var method = FindMethod(model, "Bar");

        Assert.Equal(ThisTier.ReadonlyRef, method.DeclaredThisTier);
    }

    // ─── methods without `with` retain null annotation ───────────────────

    [Fact]
    public void MethodWithoutWith_RetainsNullDeclaredThisTier()
    {
        var source = @"
class Foo
{
    void Plain() { }
    string GetName() { return """"; }
}
";
        var model = BuildModel(source);

        var plain = FindMethod(model, "Plain");
        var getName = FindMethod(model, "GetName");

        Assert.Null(plain.DeclaredThisTier);
        Assert.Null(getName.DeclaredThisTier);
    }

    // ─── mixed: class with both annotated and unannotated methods ────────

    [Fact]
    public void MixedMethods_AnnotatedAndUnannotated_ParseCorrectly()
    {
        var source = @"
class Widget
{
    void Mutate() with borrowed { }
    string Describe() with frozen { return """"; }
    void Consume() with owned { }
    void ReadOnly() with readonly ref { }
    void Plain() { }
}
";
        var model = BuildModel(source);

        Assert.Equal(ThisTier.Borrowed, FindMethod(model, "Mutate").DeclaredThisTier);
        Assert.Equal(ThisTier.Frozen, FindMethod(model, "Describe").DeclaredThisTier);
        Assert.Equal(ThisTier.Owned, FindMethod(model, "Consume").DeclaredThisTier);
        Assert.Equal(ThisTier.ReadonlyRef, FindMethod(model, "ReadOnly").DeclaredThisTier);
        Assert.Null(FindMethod(model, "Plain").DeclaredThisTier);
    }
}