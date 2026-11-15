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

    public static ParseOutcome Parse(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var diagnostics = new DiagnosticBag();

        var tokens = Lexer.Lex(sourceText, diagnostics);
        var parseResult = Parser.Parse(tokens, diagnostics);

        var all = diagnostics.ToImmutableArray().AddRange(parseResult.Diagnostics);
        return new ParseOutcome(parseResult.Root, all);
    }

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

    internal sealed record ParseOutcome(
        Storm.Runtime.SyntaxNode? Root,
        ImmutableArray<Diagnostic> Diagnostics);
}