using Mist.Common.Diagnostics;

namespace Mist.Lexing.Contracts;

public interface ILexer
{
    TokenStream Lex(Common.Source.SourceText source, DiagnosticBag diagnostics);
}