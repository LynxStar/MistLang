using Mist.Common.Diagnostics;
using Mist.Lexing.Contracts;

namespace Mist.Parsing.Contracts;

/// <summary>
/// Contract for the parsing stage: consumes a token stream and produces a parse result.
/// </summary>
public interface IParser
{
    ParseResult Parse(TokenStream tokens, DiagnosticBag diagnostics);
}
