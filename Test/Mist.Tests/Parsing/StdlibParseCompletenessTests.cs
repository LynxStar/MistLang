using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Parsing;

/// <summary>
/// Task 4 checkpoint: verifies that Array.mist and Span.mist parse completely
/// with zero MIST_PARSE_INCOMPLETE diagnostics, and that all type declarations
/// are registered in the Standard namespace scope.
///
/// Validates: Requirements 4.1, 4.2, 4.3, 4.4
/// </summary>
public sealed class StdlibParseCompletenessTests
{
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Mist.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(
            "Could not find repository root (Mist.slnx) from AppContext.BaseDirectory.");
    }

    private static (Storm.Runtime.SyntaxNode? Root, ImmutableArray<Diagnostic> Diagnostics) ParseFile(string filePath)
    {
        var source = File.ReadAllText(filePath);
        var sourceText = new SourceText(filePath, source);
        ILexer lexer = new MistLexer();
        IParser parser = new MistParser();
        var diagnostics = new DiagnosticBag();

        var tokens = lexer.Lex(sourceText, diagnostics);
        var result = parser.Parse(tokens, diagnostics);

        return (result.Root, diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ArrayMist_ParsesCompletely_NoIncomplete()
    {
        var repoRoot = FindRepoRoot();
        var arrayPath = Path.Combine(repoRoot, "Source", "Standard", "Bootstrap", "Array.mist");

        var (root, diagnostics) = ParseFile(arrayPath);

        Assert.NotNull(root);

        var incompleteDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_PARSE_INCOMPLETE")
            .ToList();

        Assert.True(
            incompleteDiags.Count == 0,
            $"Array.mist produced MIST_PARSE_INCOMPLETE: {string.Join("; ", incompleteDiags.Select(d => d.Message))}");
    }

    [Fact]
    public void SpanMist_ParsesCompletely_NoIncomplete()
    {
        var repoRoot = FindRepoRoot();
        var spanPath = Path.Combine(repoRoot, "Source", "Standard", "Bootstrap", "Span.mist");

        var (root, diagnostics) = ParseFile(spanPath);

        Assert.NotNull(root);

        var incompleteDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_PARSE_INCOMPLETE")
            .ToList();

        Assert.True(
            incompleteDiags.Count == 0,
            $"Span.mist produced MIST_PARSE_INCOMPLETE: {string.Join("; ", incompleteDiags.Select(d => d.Message))}");
    }

    [Fact]
    public void ArrayMist_TypesRegisteredInStandardNamespace()
    {
        var repoRoot = FindRepoRoot();
        var stdlibRoot = Path.Combine(repoRoot, "Source", "Standard");

        var loaderDiag = new DiagnosticBag();
        var units = StdlibLoader.LoadFrom(stdlibRoot, loaderDiag);

        ILexer lexer = new MistLexer();
        IParser parser = new MistParser();
        var parseDiag = new DiagnosticBag();
        var validUnits = ImmutableArray.CreateBuilder<CompilationUnit>();
        var validAsts = ImmutableArray.CreateBuilder<Storm.Runtime.SyntaxNode>();

        foreach (var unit in units)
        {
            var tokens = lexer.Lex(unit.Source, parseDiag);
            var parseResult = parser.Parse(tokens, parseDiag);
            if (parseResult.Root is not null)
            {
                validUnits.Add(unit);
                validAsts.Add(parseResult.Root);
            }
        }

        var compilation = new MistCompilation(validUnits.ToImmutable());
        var modelDiag = new DiagnosticBag();
        var builder = new SemanticModelBuilder();
        var model = builder.Build(
            compilation, validAsts.ToImmutable(), null,
            namespaceRoot: "Standard", projectFolder: stdlibRoot, modelDiag);

        // Assert — Array type must be registered in Standard namespace (Requirement 4.3)
        var arraySymbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == "Array"
            && s.Kind == SymbolKind.Class
            && s.DeclaredType is ClassType ct
            && ct.ContainingNamespace == "Standard");

        Assert.NotNull(arraySymbol);
        var arrayType = (ClassType)arraySymbol!.DeclaredType!;
        Assert.Equal("Array", arrayType.DisplayName);
        Assert.Equal("Standard", arrayType.ContainingNamespace);

        // Verify type parameters are registered (TValue + Length)
        Assert.True(arrayType.TypeParameters.Count >= 2,
            $"Array should have at least 2 type parameters, found {arrayType.TypeParameters.Count}");
    }

    [Fact]
    public void SpanMist_TypesRegisteredInStandardNamespace()
    {
        var repoRoot = FindRepoRoot();
        var stdlibRoot = Path.Combine(repoRoot, "Source", "Standard");

        var loaderDiag = new DiagnosticBag();
        var units = StdlibLoader.LoadFrom(stdlibRoot, loaderDiag);

        ILexer lexer = new MistLexer();
        IParser parser = new MistParser();
        var parseDiag = new DiagnosticBag();
        var validUnits = ImmutableArray.CreateBuilder<CompilationUnit>();
        var validAsts = ImmutableArray.CreateBuilder<Storm.Runtime.SyntaxNode>();

        foreach (var unit in units)
        {
            var tokens = lexer.Lex(unit.Source, parseDiag);
            var parseResult = parser.Parse(tokens, parseDiag);
            if (parseResult.Root is not null)
            {
                validUnits.Add(unit);
                validAsts.Add(parseResult.Root);
            }
        }

        var compilation = new MistCompilation(validUnits.ToImmutable());
        var modelDiag = new DiagnosticBag();
        var builder = new SemanticModelBuilder();
        var model = builder.Build(
            compilation, validAsts.ToImmutable(), null,
            namespaceRoot: "Standard", projectFolder: stdlibRoot, modelDiag);

        // Assert — Span type must be registered (Requirement 4.4)
        var spanSymbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == "Span"
            && s.Kind == SymbolKind.Struct
            && s.DeclaredType is StructType st
            && st.ContainingNamespace == "Standard");

        Assert.NotNull(spanSymbol);
        var spanType = (StructType)spanSymbol!.DeclaredType!;
        Assert.Equal("Span", spanType.DisplayName);
        Assert.Equal("Standard", spanType.ContainingNamespace);

        // Assert — ReadOnlySpan type must also be registered
        var readOnlySpanSymbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == "ReadOnlySpan"
            && s.Kind == SymbolKind.Struct
            && s.DeclaredType is StructType st
            && st.ContainingNamespace == "Standard");

        Assert.NotNull(readOnlySpanSymbol);
        var rosType = (StructType)readOnlySpanSymbol!.DeclaredType!;
        Assert.Equal("ReadOnlySpan", rosType.DisplayName);
        Assert.Equal("Standard", rosType.ContainingNamespace);
    }
}
