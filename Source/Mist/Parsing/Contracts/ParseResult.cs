using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Lexing.Contracts;

namespace Mist.Parsing.Contracts;

public sealed record ParseResult(
    Common.Source.SourceText Source,
    TokenStream Tokens,
    Storm.Runtime.SyntaxNode? Root,
    ImmutableArray<Diagnostic> Diagnostics);