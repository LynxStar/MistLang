using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;

namespace Mist.Lexing;

internal static class StormLexerDiagnostics
{
    public static void Report(
        SourceText source, string message, string? entryPath, int line, int column,
        ImmutableArray<Storm.Runtime.Token> tokens, DiagnosticBag diagnostics)
    {
        if(entryPath is null || string.Equals(entryPath, source.Path, StringComparison.Ordinal))
        {
            foreach(var token in tokens)
            {
                if(token.Kind == (int)Generated.TokenKind.Error && token.Start.Line == line && token.Start.Column == column)
                {
                    diagnostics.Admit(DiagnosticFactory.Create(
                        LexerDiagnostics.LexError, new SourceRange(new SourceIdentity(source), token.Span.Start, token.Span.Length), diagnostics.Stage,
                        [new TextValue(message)], frames: [new StormEntryFrame(token.Span.Start, token.Span.Length, token.Lexeme)]));
                    return;
                }
            }
        }

        diagnostics.ReportTyped(
            LexerDiagnostics.LexError, StormEntryLocation.Of(source, entryPath, line, column), new TextValue(message));
    }
}