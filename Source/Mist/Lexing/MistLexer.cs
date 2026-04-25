using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Lexing.Contracts;

namespace Mist.Lexing;

/// <summary>
/// Wraps the Storm-generated lexer behind the <see cref="ILexer"/> interface
/// for testability and dependency injection.
/// </summary>
public sealed class MistLexer : ILexer
{
    public TokenStream Lex(Common.Text.SourceText source, DiagnosticBag diagnostics)
    {
        // Bridge Mist's SourceText to Storm.Runtime.SourceText
        var stormSource = new Storm.Runtime.SourceText(source.Path, source.Contents);
        var stormDiagnostics = new Storm.Runtime.DiagnosticBag();

        // TODO: Call the Storm-generated lexer once the Mist grammar is fully defined.
        // For now, produce an empty token stream as a stub.
        // var stormTokenStream = Mist.Generated.MistLexer.Lex(stormSource, stormDiagnostics);

        // Map any Storm diagnostics back to Mist diagnostics
        foreach(var msg in stormDiagnostics.Messages)
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

        return new TokenStream(source, ImmutableArray<Storm.Runtime.Token>.Empty);
    }
}
