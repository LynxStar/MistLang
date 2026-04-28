using Mist.Common.Diagnostics;
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
    public TokenStream Lex(Common.Text.SourceText source, DiagnosticBag diagnostics)
    {
        var stormSource = new Storm.Runtime.SourceText(source.Path, source.Contents);
        var stormDiagnostics = new Storm.Runtime.DiagnosticBag();

        var stormTokenStream = Mist.Generated.MistLexer.Lex(stormSource, stormDiagnostics);

        // Map Storm diagnostics to Mist diagnostics
        foreach (var msg in stormDiagnostics.Messages)
        {
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_LEX_ERROR"),
                DiagnosticSeverity.Error,
                PipelineStage.Lexing,
                msg,
                Location: null,
                CompilationUnitPath: source.Path,
                AnalyzerName: null));
        }

        return new TokenStream(source, stormTokenStream.AllTokens);
    }
}
