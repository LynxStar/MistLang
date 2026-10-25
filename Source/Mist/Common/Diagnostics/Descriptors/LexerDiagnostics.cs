#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static class LexerDiagnostics
{
    public static readonly MistDiagnostic LexError = new(
        DiagnosticNumber.Mint("MIST0120"),
        Title: "Lexical error",
        Template: "{0}",
        Description: "The Storm lexer could not tokenize the source at this position and the message is the lexer's own. Correct the character sequence it names; nothing after it is parsed until the file lexes cleanly.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);
}