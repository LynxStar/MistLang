using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Lexing.Contracts;

namespace Mist.Parsing.Contracts;

/// <summary>
/// The result of parsing a single compilation unit.
/// </summary>
public sealed record ParseResult(
    Common.SourceText.SourceText Source,
    TokenStream Tokens,
    Storm.Runtime.SyntaxNode? Root,
    ImmutableArray<Diagnostic> Diagnostics);
