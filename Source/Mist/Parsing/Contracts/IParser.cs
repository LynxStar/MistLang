using Mist.Common.Diagnostics;
using Mist.Lexing.Contracts;

namespace Mist.Parsing.Contracts;

public interface IParser
{
    ParseResult Parse(TokenStream tokens, DiagnosticBag diagnostics);
}