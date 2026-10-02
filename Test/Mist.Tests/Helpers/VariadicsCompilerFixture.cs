using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Helpers;

internal static class VariadicsCompilerFixture
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();

    /// <summary>
    /// Lexes and parses a Mist source string in-process (no semantic model, no
    /// analysis, no emit) and returns the parsed AST root together with every
    /// lex/parse diagnostic. Useful for AST-shape and round-trip checks that only
    /// need the front end.
    /// </summary>
    public static ParseOutcome Parse(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var diagnostics = new DiagnosticBag();

        var tokens = Lexer.Lex(sourceText, diagnostics);
        var parseResult = Parser.Parse(tokens, diagnostics);

        var all = diagnostics.ToImmutableArray().AddRange(parseResult.Diagnostics);
        return new ParseOutcome(parseResult.Root, all);
    }

    /// <summary>
    /// Returns <c>true</c> when the parse outcome contains no lex- or parse-stage
    /// error diagnostics and produced a non-null AST root.
    /// </summary>
    public static bool ParsedCleanly(ParseOutcome outcome)
    {
        if(outcome.Root is null)
        {
            return false;
        }

        return !outcome.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Stage is PipelineStage.Lexing or PipelineStage.Parsing);
    }

    /// <summary>
    /// The result of a parse-only run: the AST root (null on parse failure) and the
    /// collected lex/parse diagnostics.
    /// </summary>
    internal sealed record ParseOutcome(
        Storm.Runtime.SyntaxNode? Root,
        ImmutableArray<Diagnostic> Diagnostics);
}