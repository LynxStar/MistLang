using System.Collections.Immutable;

namespace Mist.Lexing.Contracts;

/// <summary>
/// An ordered sequence of tokens produced by the lexer from a single source input.
/// Provides both the full token list (including hidden tokens) and a filtered
/// default-channel view for the parser.
/// </summary>
public sealed class TokenStream
{
    /// <summary>
    /// The source text that was lexed.
    /// </summary>
    public Common.Text.SourceText Source { get; }

    /// <summary>
    /// All tokens including hidden-channel tokens.
    /// </summary>
    public ImmutableArray<Storm.Runtime.Token> AllTokens { get; }

    /// <summary>
    /// Tokens on the default channel only (hidden tokens filtered out).
    /// This is the view the parser consumes.
    /// </summary>
    public ImmutableArray<Storm.Runtime.Token> DefaultChannelTokens { get; }

    public TokenStream(Common.Text.SourceText source, ImmutableArray<Storm.Runtime.Token> tokens)
    {
        Source = source;
        AllTokens = tokens;
        DefaultChannelTokens = tokens
            .Where(t => t.Channel == Storm.Runtime.TokenChannel.Default)
            .ToImmutableArray();
    }
}