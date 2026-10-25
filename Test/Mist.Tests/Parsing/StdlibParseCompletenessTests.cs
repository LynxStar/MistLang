using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Task 4 checkpoint: verifies that Array.mist and Span.mist parse completely
/// with zero MIST_PARSE_INCOMPLETE diagnostics.
///
/// Validates: Requirements 4.1, 4.2
/// </summary>
[Trait("Tier", "3")]
public sealed class StdlibParseCompletenessTests
{
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while(dir is not null)
        {
            if(File.Exists(Path.Combine(dir, "Mist.slnx")))
            {
                return dir;
            }

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
            .Where(d => d.Number.Value == "MIST_PARSE_INCOMPLETE")
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
            .Where(d => d.Number.Value == "MIST_PARSE_INCOMPLETE")
            .ToList();

        Assert.True(
            incompleteDiags.Count == 0,
            $"Span.mist produced MIST_PARSE_INCOMPLETE: {string.Join("; ", incompleteDiags.Select(d => d.Message))}");
    }
}