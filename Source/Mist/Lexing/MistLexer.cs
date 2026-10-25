using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Lexing.Contracts;

namespace Mist.Lexing;

/// <summary>
/// Wraps the Storm-generated lexer behind the <see cref="ILexer"/> interface.
///
/// The Storm-generated lexer now handles interpolated strings natively via
/// lexer modes, so this wrapper simply delegates to the generated lexer
/// and maps Storm diagnostics to Mist diagnostics.
/// </summary>
public sealed class MistLexer : ILexer
{
    public TokenStream Lex(SourceText source, DiagnosticBag diagnostics)
    {
        diagnostics.Stage = PipelineStage.Lexing;
        var stormSource = new Storm.Runtime.SourceText(source.Path, source.Contents);
        var stormDiagnostics = new Storm.Runtime.DiagnosticBag();

        var stormTokenStream = Generated.MistLexer.Lex(stormSource, stormDiagnostics);

        // Map Storm diagnostics to Mist diagnostics
        foreach(var entry in stormDiagnostics.Entries)
        {
            var location = (entry.Line > 0 && entry.Column > 0)
                ? new LinePositionSpan(
                    new LinePosition(entry.Line, entry.Column),
                    new LinePosition(entry.Line, entry.Column))
                : default;

            diagnostics.Report(LexerDiagnostics.LexError, location, entry.FilePath ?? source.Path, entry.Message);
        }

        return new TokenStream(source, stormTokenStream.AllTokens);
    }
}