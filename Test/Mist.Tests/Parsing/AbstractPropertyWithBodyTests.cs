// Feature: abstract-type-unification
// Validates: Requirement 1.10

using Mist.Common.Diagnostics;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Verifies that property slots with bodies inside abstract type declarations
/// produce the error diagnostic: "Property slots cannot have default implementations.
/// Only methods can have defaults."
///
/// Validates: Requirement 1.10
/// </summary>
public class AbstractPropertyWithBodyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static IReadOnlyList<Diagnostic> ParseAndGetDiagnostics(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        Parser.Parse(tokens, bag);
        return bag.ToImmutableArray();
    }

    [Fact]
    public void PropertyWithBlockBody_EmitsDiagnostic()
    {
        var source = """
            abstract IFoo {
                string Name { return "hello"; }
            }
            """;

        var diagnostics = ParseAndGetDiagnostics(source);

        var propertyDefaultDiag = diagnostics.FirstOrDefault(d =>
            d.Id.Value == "MIST_PARSE_PROPERTY_DEFAULT");

        Assert.NotNull(propertyDefaultDiag);
        Assert.Equal(DiagnosticSeverity.Error, propertyDefaultDiag.Severity);
        Assert.Equal(
            "Property slots cannot have default implementations. Only methods can have defaults.",
            propertyDefaultDiag.Message);
    }

    [Fact]
    public void PropertyWithGetAccessorBody_EmitsDiagnostic()
    {
        var source = """
            abstract IFoo {
                string Name { get { return "hello"; } }
            }
            """;

        var diagnostics = ParseAndGetDiagnostics(source);

        var propertyDefaultDiag = diagnostics.FirstOrDefault(d =>
            d.Id.Value == "MIST_PARSE_PROPERTY_DEFAULT");

        Assert.NotNull(propertyDefaultDiag);
        Assert.Equal(DiagnosticSeverity.Error, propertyDefaultDiag.Severity);
        Assert.Equal(
            "Property slots cannot have default implementations. Only methods can have defaults.",
            propertyDefaultDiag.Message);
    }

    [Fact]
    public void ValidPropertySlot_NoDiagnostic()
    {
        var source = """
            abstract IFoo {
                string Name { get; }
            }
            """;

        var diagnostics = ParseAndGetDiagnostics(source);

        var propertyDefaultDiag = diagnostics.FirstOrDefault(d =>
            d.Id.Value == "MIST_PARSE_PROPERTY_DEFAULT");

        Assert.Null(propertyDefaultDiag);
    }

    [Fact]
    public void ValidPropertySlotWithGetSet_NoDiagnostic()
    {
        var source = """
            abstract IFoo {
                string Name { get; set; }
            }
            """;

        var diagnostics = ParseAndGetDiagnostics(source);

        var propertyDefaultDiag = diagnostics.FirstOrDefault(d =>
            d.Id.Value == "MIST_PARSE_PROPERTY_DEFAULT");

        Assert.Null(propertyDefaultDiag);
    }

    [Fact]
    public void MethodWithBody_NoDiagnostic()
    {
        var source = """
            abstract IFoo {
                string Name() { return "hello"; }
            }
            """;

        var diagnostics = ParseAndGetDiagnostics(source);

        var propertyDefaultDiag = diagnostics.FirstOrDefault(d =>
            d.Id.Value == "MIST_PARSE_PROPERTY_DEFAULT");

        Assert.Null(propertyDefaultDiag);
    }

    [Fact]
    public void MethodSlotWithoutBody_NoDiagnostic()
    {
        var source = """
            abstract IFoo {
                string Name();
            }
            """;

        var diagnostics = ParseAndGetDiagnostics(source);

        var propertyDefaultDiag = diagnostics.FirstOrDefault(d =>
            d.Id.Value == "MIST_PARSE_PROPERTY_DEFAULT");

        Assert.Null(propertyDefaultDiag);
    }

    [Fact]
    public void PrimitiveTypePropertyWithBody_EmitsDiagnostic()
    {
        var source = """
            abstract IFoo {
                int Value { return 42; }
            }
            """;

        var diagnostics = ParseAndGetDiagnostics(source);

        var propertyDefaultDiag = diagnostics.FirstOrDefault(d =>
            d.Id.Value == "MIST_PARSE_PROPERTY_DEFAULT");

        Assert.NotNull(propertyDefaultDiag);
        Assert.Equal(
            "Property slots cannot have default implementations. Only methods can have defaults.",
            propertyDefaultDiag.Message);
    }

    [Fact]
    public void GenericTypePropertyWithBody_EmitsDiagnostic()
    {
        var source = """
            abstract IFoo {
                List<string> Items { return new List<string>(); }
            }
            """;

        var diagnostics = ParseAndGetDiagnostics(source);

        var propertyDefaultDiag = diagnostics.FirstOrDefault(d =>
            d.Id.Value == "MIST_PARSE_PROPERTY_DEFAULT");

        Assert.NotNull(propertyDefaultDiag);
        Assert.Equal(
            "Property slots cannot have default implementations. Only methods can have defaults.",
            propertyDefaultDiag.Message);
    }

    [Fact]
    public void ClassPropertyWithBody_NoDiagnostic()
    {
        // Properties with bodies inside classes are valid — this diagnostic
        // only fires inside abstract type bodies.
        var source = """
            class Foo {
                string Name { get { return "hello"; } }
            }
            """;

        var diagnostics = ParseAndGetDiagnostics(source);

        var propertyDefaultDiag = diagnostics.FirstOrDefault(d =>
            d.Id.Value == "MIST_PARSE_PROPERTY_DEFAULT");

        Assert.Null(propertyDefaultDiag);
    }
}
