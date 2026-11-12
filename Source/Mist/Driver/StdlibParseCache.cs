using System.Collections.Immutable;
using Mist.Lexing.Contracts;

namespace Mist.Driver;

/// <summary>
/// An entry is recorded only when it covers every stdlib unit: the lexer and parser are injected per driver
/// while <see cref="Shared"/> serves every driver in the process, and cached trees follow the loader's unit
/// order, so a short entry would attach a tree to the wrong source on every later compile.
/// </summary>
internal sealed class StdlibParseCache
{
    internal static StdlibParseCache Shared { get; } = new();

    private Entry? entry;

    internal sealed record Entry(
        ImmutableArray<TokenStream> Tokens,
        ImmutableArray<Storm.Runtime.SyntaxNode> Asts);

    internal Entry? TryGet(int stdlibUnitCount)
    {
        var current = Volatile.Read(ref entry);
        return current is not null && current.Asts.Length == stdlibUnitCount ? current : null;
    }

    internal void Offer(
        int stdlibUnitCount,
        ImmutableArray<TokenStream> tokens,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts)
    {
        if(tokens.Length != stdlibUnitCount || asts.Length != stdlibUnitCount)
        {
            return;
        }

        _ = Interlocked.CompareExchange(ref entry, new Entry(tokens, asts), null);
    }
}