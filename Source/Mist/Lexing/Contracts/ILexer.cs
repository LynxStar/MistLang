using Mist.Common.Diagnostics;

namespace Mist.Lexing.Contracts;

/// <summary>
/// Contract for the lexing stage: consumes source text and produces a token stream.
/// </summary>
public interface ILexer
{
    TokenStream Lex(Common.Text.SourceText source, DiagnosticBag diagnostics);
}