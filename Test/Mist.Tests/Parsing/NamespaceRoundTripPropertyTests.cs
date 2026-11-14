// Feature: inferred-namespaces, Property 16: Pretty Printer Namespace Round-Trip
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

    [Property(Arbitrary = new[] { typeof(MistMultiNamespaceGenerator) }, MaxTest = 100)]
    public Property MultiNamespaceSource_RoundTripsThrough_PrintParsePrint(
        MultiNamespaceSource input)
    {
        return RoundTripProperty(input.Source);
    }

    [Property(Arbitrary = new[] { typeof(MistMultiNamespaceGenerator) }, MaxTest = 100)]
    public Property MixedNamespaceSource_RoundTripsThrough_PrintParsePrint(
        MixedNamespaceSource input)
    {
        return RoundTripProperty(input.Source);
    }

    private static Property RoundTripProperty(string source)
    {
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

        var printed1 = MistPrettyPrinter.Print(cu1, source);
        if(string.IsNullOrWhiteSpace(printed1) && cu1.Items.Length == 0 && cu1.Usings.Length == 0)
        {
            return true.ToProperty().Label("Skipped: empty source");
        }

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

        var printed2 = MistPrettyPrinter.Print(cu2, printed1);

        return (printed1 == printed2).ToProperty()
            .Label($"Mismatch.\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }

    [Fact]
    public void FileScopedNamespace_RoundTrips()
    {
        var source = "namespace MyApp.Models;\nclass Player {\n}\n";
        AssertRoundTrip(source);
    }

    [Fact]
    public void BlockNamespace_RoundTrips()
    {
        var source = "namespace MyApp {\n    class Player {\n    }\n}\n";
        AssertRoundTrip(source);
    }

    [Fact]
    public void MultipleNamespaces_RoundTrip()
    {
        var source = "namespace First;\nclass A {\n}\n\nnamespace Second {\n    class B {\n    }\n}\n";
        AssertRoundTrip(source);
    }

    [Fact]
    public void UsingAndNamespace_RoundTrips()
    {
        var source = "using System;\n\nnamespace MyApp;\nclass Program {\n}\n";
        AssertRoundTrip(source);
    }

    [Fact]
    public void TypesOnly_RoundTrips()
    {
        var source = "class A {\n}\nclass B {\n}\n";
        AssertRoundTrip(source);
    }

    [Fact]
    public void EmptyFile_RoundTrips()
    {
        var source = "";
        var result = ParseSource(source);
        Assert.NotNull(result.Root);
        _ = Assert.IsType<CompilationUnitNode>(result.Root);

        var printed = MistPrettyPrinter.Print(result.Root, source);
        Assert.True(string.IsNullOrWhiteSpace(printed));
    }

    private static void AssertRoundTrip(string source)
    {
        var result1 = ParseSource(source);
        Assert.NotNull(result1.Root);
        _ = Assert.IsType<CompilationUnitNode>(result1.Root);
        Assert.Empty(result1.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var printed1 = MistPrettyPrinter.Print(result1.Root, source);
        Assert.False(string.IsNullOrWhiteSpace(printed1), "Pretty-printed output should not be empty");

        var result2 = ParseSource(printed1);
        Assert.NotNull(result2.Root);
        _ = Assert.IsType<CompilationUnitNode>(result2.Root);
        var errors2 = result2.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors2);

        var printed2 = MistPrettyPrinter.Print(result2.Root, printed1);

        Assert.Equal(printed1, printed2);
    }
}