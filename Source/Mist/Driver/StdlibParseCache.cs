using System.Collections.Immutable;
using Mist.Lexing.Contracts;

namespace Mist.Driver;

/// <summary>
/// The standard library's token streams and syntax trees, lexed and parsed once and reused by
/// every later compile. An entry is recorded only when it holds one of each for every stdlib unit:
/// the lexer and parser are injected per driver while <see cref="Shared"/> serves every driver in
/// the process, and both pipelines pair syntax trees with units by index, so a short entry would
/// misalign every compile that followed it.
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