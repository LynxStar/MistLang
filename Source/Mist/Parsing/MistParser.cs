using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Parsing;

/// <summary>
/// Wraps the Storm-generated parser behind the <see cref="IParser"/> interface,
/// selecting the appropriate entry rule for Mist compilation units.
/// </summary>
public sealed class MistParser : IParser
{
    public ParseResult Parse(TokenStream tokens, DiagnosticBag diagnostics)
    {
        // Bridge to Storm-generated parser.
        // TODO: Call the Storm-generated parser once the Mist grammar is fully defined.
        // For now, return a stub result with no AST root.
        // var stormDiagnostics = new Storm.Runtime.DiagnosticBag();
        // var stormTokenStream = new Storm.Runtime.TokenStream(
        //     new Storm.Runtime.SourceText(tokens.Source.Path, tokens.Source.Contents),
        //     tokens.AllTokens);
        // var root = Mist.Generated.MistParser.ParseCompilationUnit(stormTokenStream, stormDiagnostics);

        return new ParseResult(
            tokens.Source,
            tokens,
            Root: null,
            Diagnostics: ImmutableArray<Diagnostic>.Empty);
    }
}
