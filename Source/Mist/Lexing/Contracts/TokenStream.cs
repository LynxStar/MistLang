using System.Collections.Immutable;

namespace Mist.Lexing.Contracts;

public sealed class TokenStream
{
    public Common.Source.SourceText Source { get; }

    public ImmutableArray<Storm.Runtime.Token> AllTokens { get; }

    public ImmutableArray<Storm.Runtime.Token> DefaultChannelTokens { get; }

    public TokenStream(Common.Source.SourceText source, ImmutableArray<Storm.Runtime.Token> tokens)
    {
        Source = source;
        AllTokens = tokens;
        DefaultChannelTokens = tokens
            .Where(t => t.Channel == Storm.Runtime.TokenChannel.Default)
            .ToImmutableArray();
    }
}