using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests verifying that the semantic model correctly records
/// <see cref="ReadonlyStatus.ExplicitReadonly"/> on local variable symbols
/// declared with the <c>readonly</c> modifier.
///
/// Requirements: 2.1–2.4
/// </summary>
public class ReadonlyLocalSemanticTests
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ISemanticModel BuildSemanticModel(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var parseResult = Parser.Parse(tokens, bag);

        Assert.NotNull(parseResult.Root);
        Assert.Empty(parseResult.Diagnostics);

        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    private static Symbol FindLocalVariable(ISemanticModel model, string name)
    {
        var symbol = model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.LocalVariable);
        Assert.NotNull(symbol);
        return symbol!;
    }

    // ─── Requirement 2.1: readonly var x = 7; → ExplicitReadonly ─────────

    [Fact]
    public void ReadonlyInferredVar_HasExplicitReadonlyStatus()
    {
        var source = "class T { public void M() { readonly var x = 7; } }";
        var model = BuildSemanticModel(source);

        var symbol = FindLocalVariable(model, "x");
        Assert.Equal(ReadonlyStatus.ExplicitReadonly, symbol.ReadonlyStatus);
    }

    // ─── Requirement 2.1: readonly int x = 7; → ExplicitReadonly ─────────

    [Fact]
    public void ReadonlyTypedVar_HasExplicitReadonlyStatus()
    {
        var source = "class T { public void M() { readonly int x = 7; } }";
        var model = BuildSemanticModel(source);

        var symbol = FindLocalVariable(model, "x");
        Assert.Equal(ReadonlyStatus.ExplicitReadonly, symbol.ReadonlyStatus);
    }

    // ─── Requirement 2.2: non-readonly var does NOT have ExplicitReadonly ─

    [Fact]
    public void NonReadonlyInferredVar_DoesNotHaveExplicitReadonlyStatus()
    {
        var source = "class T { public void M() { var x = 7; } }";
        var model = BuildSemanticModel(source);

        var symbol = FindLocalVariable(model, "x");
        Assert.NotEqual(ReadonlyStatus.ExplicitReadonly, symbol.ReadonlyStatus);
    }

    [Fact]
    public void NonReadonlyTypedVar_DoesNotHaveExplicitReadonlyStatus()
    {
        var source = "class T { public void M() { int x = 7; } }";
        var model = BuildSemanticModel(source);

        var symbol = FindLocalVariable(model, "x");
        Assert.NotEqual(ReadonlyStatus.ExplicitReadonly, symbol.ReadonlyStatus);
    }

    // ─── Requirement 2.3: readonly var (a, b) = (1, 2); → both ExplicitReadonly ─

    [Fact]
    public void ReadonlyVarTupleDeconstruction_SetsExplicitReadonlyOnAllSymbols()
    {
        var source = "class T { public void M() { readonly var (a, b) = (1, 2); } }";
        var model = BuildSemanticModel(source);

        var symbolA = FindLocalVariable(model, "a");
        var symbolB = FindLocalVariable(model, "b");

        Assert.Equal(ReadonlyStatus.ExplicitReadonly, symbolA.ReadonlyStatus);
        Assert.Equal(ReadonlyStatus.ExplicitReadonly, symbolB.ReadonlyStatus);
    }

    // ─── Requirement 2.3: readonly (int a, string b) = (1, "hi"); → both ExplicitReadonly ─

    [Fact]
    public void ReadonlyTypedTupleDeconstruction_SetsExplicitReadonlyOnAllSymbols()
    {
        var source = """class T { public void M() { readonly (int a, string b) = (1, "hi"); } }""";
        var model = BuildSemanticModel(source);

        var symbolA = FindLocalVariable(model, "a");
        var symbolB = FindLocalVariable(model, "b");

        Assert.Equal(ReadonlyStatus.ExplicitReadonly, symbolA.ReadonlyStatus);
        Assert.Equal(ReadonlyStatus.ExplicitReadonly, symbolB.ReadonlyStatus);
    }

    // ─── Additional: non-readonly tuple deconstruction does NOT have ExplicitReadonly ─

    [Fact]
    public void NonReadonlyVarTupleDeconstruction_DoesNotHaveExplicitReadonly()
    {
        var source = "class T { public void M() { var (a, b) = (1, 2); } }";
        var model = BuildSemanticModel(source);

        var symbolA = FindLocalVariable(model, "a");
        var symbolB = FindLocalVariable(model, "b");

        Assert.NotEqual(ReadonlyStatus.ExplicitReadonly, symbolA.ReadonlyStatus);
        Assert.NotEqual(ReadonlyStatus.ExplicitReadonly, symbolB.ReadonlyStatus);
    }
}
