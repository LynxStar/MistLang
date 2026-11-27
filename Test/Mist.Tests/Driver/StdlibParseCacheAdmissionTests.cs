using System.Collections.Immutable;
using Mist.Common.Source;
using Mist.Driver;
using Mist.Generated;
using Mist.Lexing.Contracts;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class StdlibParseCacheAdmissionTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void Offer_WithOneShortList_LeavesRoomForACompleteEntry(int tokenStreamCount, int astCount)
    {
        const int stdlibUnitCount = 1;
        var cache = new StdlibParseCache();
        var tokenStream = new TokenStream(new SourceText("Standard.mist", ""), []);
        var root = new CompilationUnitNode();
        var tokens = ImmutableArray.Create(tokenStream);
        var asts = ImmutableArray.Create<SyntaxNode>(root);

        cache.Offer(stdlibUnitCount, tokens[..tokenStreamCount], asts[..astCount]);
        cache.Offer(stdlibUnitCount, tokens, asts);

        var entry = cache.TryGet(stdlibUnitCount);
        Assert.NotNull(entry);
        Assert.Same(tokenStream, Assert.Single(entry.Tokens));
        Assert.Same(root, Assert.Single(entry.Asts));
    }
}