#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors for the lexer stage.
/// </summary>
public static class LexerDiagnostics
{
    /// <summary>
    /// A lexical error was encountered during tokenization.
    /// The message is provided by the Storm lexer runtime.
    /// </summary>
    public static readonly DiagnosticDescriptor LexError = new(
        new DiagnosticId("MIST_LEX_ERROR"),
        DiagnosticSeverity.Error,
        PipelineStage.Lexing,
        "{0}");
}