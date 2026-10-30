using System.Globalization;
using Mist.Generated;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class SizedArrayLengthTests
{
    [Theory]
    [InlineData("1_0", "10")]
    [InlineData("3u", "3")]
    [InlineData("3UL", "3")]
    [InlineData("2_147_483_647", "2147483647")]
    public void ASizedLengthWithASeparatorOrSuffix_IsTheSameTypeAsItsPlainSpelling(string spelled, string plain)
    {
        var f = Fragments();

        var array = Assert.IsType<ConstructedType>(f.Builder.Resolver.Resolve(IntArray(spelled), f.Scope));

        Assert.Same(f.Builder.Resolver.Resolve(IntArray(plain), f.Scope), array);
        Assert.Equal(long.Parse(plain, CultureInfo.InvariantCulture), Assert.IsType<LiftedType>(array.TypeArguments[1]).Value);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("2147483648")]
    [InlineData("3000000000u")]
    [InlineData("18446744073709551616")]
    public void ASizedLengthNoIntHolds_IsRefusedRatherThanReadAsZero(string size)
    {
        var f = Fragments();

        _ = Assert.IsType<ErrorType>(f.Builder.Resolver.Resolve(IntArray(size), f.Scope));
        Assert.Equal("MIST0377", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    private static BindingFragments Fragments()
    {
        var f = new BindingFragments();
        f.Register(f.Built.Type("Array<,>"));
        return f;
    }

    private static PrimitiveSingleTypeRefSingleTypeRefNode IntArray(string size)
    {
        var rank = BindingFragments.At<SizedArrayRankArrayRankSuffixNode>();
        rank.Size = BindingFragments.Token(size);
        return new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode(), ArrayRank = rank };
    }
}