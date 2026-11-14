using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Lexing.Contracts;

namespace Mist.Lexing;

/// <summary>
/// Storm's lexer modes handle interpolated strings, so nothing here post-processes the tokens.
/// </summary>
public sealed class MistLexer : ILexer
{
    public TokenStream Lex(SourceText source, DiagnosticBag diagnostics)
    {
        diagnostics.Stage = PipelineStage.Lexing;
        var stormSource = new Storm.Runtime.SourceText(source.Path, source.Contents);
        var stormDiagnostics = new Storm.Runtime.DiagnosticBag();

        var stormTokenStream = Generated.MistLexer.Lex(stormSource, stormDiagnostics);

        foreach(var entry in stormDiagnostics.Entries)
        {
            StormLexerDiagnostics.Report(source, entry.Message, entry.FilePath, entry.Line, entry.Column, stormTokenStream.AllTokens, diagnostics);
        }

        return new TokenStream(source, stormTokenStream.AllTokens);
    }
}