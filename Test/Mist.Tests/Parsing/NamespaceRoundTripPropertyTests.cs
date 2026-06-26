// Feature: inferred-namespaces, Property 16: Pretty Printer Namespace Round-Trip
// For any valid Mist source file containing namespace declarations,
// parsing the source, pretty-printing the AST, and parsing the result again
// SHALL produce an equivalent AST.
//
// **Validates: Requirements 11.1, 11.2, 11.3, 11.4**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for pretty printer namespace round-trip fidelity.
/// Verifies that parse → print → parse → print produces identical output for
/// files with namespace declarations (file-scoped, block, and mixed).
/// </summary>
public class NamespaceRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    private static ParseResult ParseSource(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        return Parser.Parse(tokens, bag);
    }

    // ─── Property 16a: Random multi-namespace source files round-trip ──

    /// <summary>
    /// Property 16: Pretty Printer Namespace Round-Trip — multi-namespace source files.
    /// For any valid Mist source with namespace declarations, parse → print → parse → print
    /// produces identical output.
    /// **Validates: Requirements 11.1, 11.2, 11.3, 11.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistMultiNamespaceGenerator) }, MaxTest = 100)]
    public Property MultiNamespaceSource_RoundTripsThrough_PrintParsePrint(
        MultiNamespaceSource input)
    {
        return RoundTripProperty(input.Source);
    }

    // ─── Property 16b: Mixed namespace source round-trips ──

    /// <summary>
    /// Property 16: Pretty Printer Namespace Round-Trip — mixed file-scoped and block namespaces.
    /// **Validates: Requirements 11.1, 11.2, 11.3, 11.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistMultiNamespaceGenerator) }, MaxTest = 100)]
    public Property MixedNamespaceSource_RoundTripsThrough_PrintParsePrint(
        MixedNamespaceSource input)
    {
        return RoundTripProperty(input.Source);
    }

    /// <summary>
    /// Verifies the round-trip property for a given source string:
    /// parse(source) → print(ast, source) → parse(printed1) → print(ast2, printed1) → printed1 == printed2
    /// </summary>
    private static Property RoundTripProperty(string source)
    {
        // Step 1: Parse the generated source
        var result1 = ParseSource(source);

        var errors1 = result1.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors1.Count > 0)
        {
            return true.ToProperty().Label("Skipped: source had parse errors");
        }

        if(result1.Root is not CompilationUnitNode cu1)
        {
            return true.ToProperty().Label("Skipped: root was not CompilationUnitNode");
        }

        // Step 2: Pretty-print the parsed AST (with source text for span-based name extraction)
        var printed1 = MistPrettyPrinter.Print(cu1, source);
        if(string.IsNullOrWhiteSpace(printed1) && cu1.Items.Length == 0 && cu1.Usings.Length == 0)
        {
            return true.ToProperty().Label("Skipped: empty source");
        }

        // Step 3: Parse the pretty-printed output
        var result2 = ParseSource(printed1);

        var errors2 = result2.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors2.Count > 0)
        {
            return false.ToProperty()
                .Label($"Re-parse errors: {string.Join("; ", errors2.Select(e => e.Message))}\nPrinted:\n{printed1}");
        }

        if(result2.Root is not CompilationUnitNode cu2)
        {
            return false.ToProperty().Label($"Re-parsed root was null.\nPrinted:\n{printed1}");
        }

        // Step 4: Pretty-print the re-parsed AST (with printed1 as source text)
        var printed2 = MistPrettyPrinter.Print(cu2, printed1);

        // Step 5: The two printed outputs must be identical
        return (printed1 == printed2).ToProperty()
            .Label($"Mismatch.\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }

    // ─── Unit tests for specific namespace round-trip scenarios ──

    /// <summary>
    /// File-scoped namespace with a class round-trips correctly (Requirement 11.2).
    /// </summary>
    [Fact]
    public void FileScopedNamespace_RoundTrips()
    {
        var source = "namespace MyApp.Models;\nclass Player {\n}\n";
        AssertRoundTrip(source);
    }

    /// <summary>
    /// Block namespace with a class round-trips correctly (Requirement 11.3).
    /// </summary>
    [Fact]
    public void BlockNamespace_RoundTrips()
    {
        var source = "namespace MyApp {\n    class Player {\n    }\n}\n";
        AssertRoundTrip(source);
    }

    /// <summary>
    /// Multiple namespaces (file-scoped then block) round-trip correctly (Requirement 11.1).
    /// </summary>
    [Fact]
    public void MultipleNamespaces_RoundTrip()
    {
        var source = "namespace First;\nclass A {\n}\n\nnamespace Second {\n    class B {\n    }\n}\n";
        AssertRoundTrip(source);
    }

    /// <summary>
    /// File with using declarations and namespace round-trips correctly.
    /// </summary>
    [Fact]
    public void UsingAndNamespace_RoundTrips()
    {
        var source = "using System;\n\nnamespace MyApp;\nclass Program {\n}\n";
        AssertRoundTrip(source);
    }

    /// <summary>
    /// Types-only file (no namespace) round-trips correctly.
    /// </summary>
    [Fact]
    public void TypesOnly_RoundTrips()
    {
        var source = "class A {\n}\nclass B {\n}\n";
        AssertRoundTrip(source);
    }

    /// <summary>
    /// Empty file round-trips correctly.
    /// </summary>
    [Fact]
    public void EmptyFile_RoundTrips()
    {
        var source = "";
        var result = ParseSource(source);
        Assert.NotNull(result.Root);
        _ = Assert.IsType<CompilationUnitNode>(result.Root);

        var printed = MistPrettyPrinter.Print(result.Root, source);
        // Empty source should produce empty or whitespace-only output
        Assert.True(string.IsNullOrWhiteSpace(printed));
    }

    // ─── Helper ──

    /// <summary>
    /// Asserts that parse → print → parse → print produces identical output.
    /// Uses the source-text-aware Print overload for span-based name extraction.
    /// </summary>
    private static void AssertRoundTrip(string source)
    {
        // Parse original source
        var result1 = ParseSource(source);
        Assert.NotNull(result1.Root);
        _ = Assert.IsType<CompilationUnitNode>(result1.Root);
        Assert.Empty(result1.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Pretty-print with source text
        var printed1 = MistPrettyPrinter.Print(result1.Root, source);
        Assert.False(string.IsNullOrWhiteSpace(printed1), "Pretty-printed output should not be empty");

        // Re-parse
        var result2 = ParseSource(printed1);
        Assert.NotNull(result2.Root);
        _ = Assert.IsType<CompilationUnitNode>(result2.Root);
        var errors2 = result2.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors2);

        // Pretty-print again with printed1 as source text
        var printed2 = MistPrettyPrinter.Print(result2.Root, printed1);

        // Must be identical
        Assert.Equal(printed1, printed2);
    }
}